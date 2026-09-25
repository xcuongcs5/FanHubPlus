using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FanHub.PaymentWalletService.Api;
using FanHub.PaymentWalletService.Data;
using FanHub.PaymentWalletService.Providers;
using FanHub.Shared.Contracts.Events;
using Microsoft.EntityFrameworkCore;

namespace FanHub.PaymentWalletService.Services;

public sealed class PaymentService(PaymentDbContext db, IVnPayGateway gateway, IConfiguration config)
{
    public static ApiException Conflict(string code) => new(409, code, code.Replace('_', ' '));
    public static ApiException Missing() => new(404, "not_found", "Record not found.");
    public Task<int> LockAsync(string resource, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
        IF @result < 0 THROW 51000, 'Payment lock timeout', 1;
        """, ct);
    public static byte[] Hash(object value) => SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
    public static void Money(decimal amount)
    {
        if (amount <= 0 || amount > 9999999999m || decimal.Truncate(amount) != amount)
            throw new ApiException(400, "invalid_amount", "Amount must be a positive whole VND amount within provider limits.");
    }
    public void Emit<T>(Guid aggregate, long version, T message) where T : notnull => db.Outbox.Add(new OutboxMessage
        { AggregateId = aggregate, AggregateVersion = version, EventType = typeof(T).Name, Payload = JsonSerializer.Serialize(message) });
    private void Provider(string provider)
    {
        if (provider == "Wallet") return;
        if (provider != "VNPay" || !config.GetValue("VnPay:Enabled", false))
            throw new ApiException(503, "provider_unavailable", "This provider is not enabled for checkout.");
    }
    public async Task<Wallet> WalletAsync(Guid user, CancellationToken ct)
    {
        await LockAsync($"wallet:{user}", ct);
        var wallet = await db.Wallets.SingleOrDefaultAsync(x => x.UserId == user && x.Currency == "VND", ct);
        if (wallet is null) { wallet = new Wallet { UserId = user }; db.Wallets.Add(wallet); }
        return wallet;
    }
    private async Task ActiveAsync(Guid user, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(x => x.UserId == user && x.Status != "Active", ct))
            throw new ApiException(403, "inactive_user", "Account is inactive.");
    }
    public async Task<PaymentTransaction> CreateAsync(Guid user, IntentRequest? intent, DepositRequest? deposit, string ip, CancellationToken ct)
    {
        var provider = intent?.Provider ?? deposit!.Provider;
        Provider(provider);
        var key = intent?.IdempotencyKey ?? deposit!.IdempotencyKey;
        var hash = Hash(new { intent, deposit });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync($"intent-user:{user}", ct);
        await ActiveAsync(user, ct);
        var previous = await db.Transactions.SingleOrDefaultAsync(x => x.UserId == user && x.IdempotencyKey == key, ct);
        if (previous is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(previous.RequestHash, hash)) throw Conflict("idempotency_key_reused");
            return previous;
        }
        var utc = DateTime.UtcNow;
        var now = new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        var p = new PaymentTransaction { UserId = user, Provider = provider, IdempotencyKey = key, RequestHash = hash,
            MerchantReference = Guid.NewGuid().ToString("N"), CreatedAt = now, ExpiresAt = now.AddMinutes(15), NextQueryAt = now.AddMinutes(2) };
        if (intent is not null)
        {
            await LockAsync($"booking:{intent.BookingId}", ct);
            var booking = await db.Bookings.FindAsync([intent.BookingId], ct) ?? throw Missing();
            p.BookingId = booking.BookingId; p.Purpose = intent.UpgradeId is null ? "Booking" : "Upgrade";
            if (intent.UpgradeId is Guid upgradeId)
            {
                var upgrade = await db.Upgrades.FindAsync([upgradeId], ct) ?? throw Missing();
                if (upgrade.BookingId != booking.BookingId || upgrade.UserId != user) throw Missing();
                if (upgrade.Status != "Pending" || booking.Status != "UpgradePending") throw Conflict("upgrade_not_payable");
                p.UpgradeId = upgradeId; p.Amount = upgrade.Amount; p.Currency = upgrade.Currency;
                p.ExpiresAt = upgrade.ExpiresAt < p.ExpiresAt ? upgrade.ExpiresAt : p.ExpiresAt;
            }
            else
            {
                if (booking.UserId != user) throw Missing();
                if (booking.Status is not ("Reserved" or "PaymentPending")) throw Conflict("booking_not_payable");
                p.Amount = booking.Amount; p.Currency = booking.Currency;
                p.ExpiresAt = booking.ExpiresAt < p.ExpiresAt ? booking.ExpiresAt : p.ExpiresAt;
            }
            // One checkout per quote, including unresolved/failed attempts. Never collect twice while an old URL can settle late.
            if (await db.Transactions.AnyAsync(x => x.BookingId == p.BookingId && x.UpgradeId == p.UpgradeId, ct))
                throw Conflict("intent_already_exists");
            if (p.ExpiresAt <= now.AddSeconds(60)) throw Conflict("quote_expired");
        }
        else
        {
            p.Purpose = "Deposit"; p.Amount = deposit!.Amount;
            if (p.Amount < 10000 || p.Amount > 100000000) throw new ApiException(400, "invalid_deposit", "Deposit must be between 10,000 and 100,000,000 VND.");
        }
        Money(p.Amount);
        if (p.Currency != "VND") throw new ApiException(400, "unsupported_currency", "Only VND is supported.");
        if (p.Purpose == "Deposit" || provider == "Wallet")
        {
            var wallet = await WalletAsync(user, ct);
            if (wallet.Status != "Active") throw Conflict("wallet_not_active");
            p.WalletId = wallet.WalletId;
            if (provider == "Wallet")
            {
                if (wallet.Balance < p.Amount) throw Conflict("insufficient_balance");
                wallet.Balance -= p.Amount; wallet.UpdatedAt = now;
                db.Ledgers.Add(new WalletLedger { WalletId = wallet.WalletId, TransactionId = p.TransactionId,
                    EntryType = "Payment", Amount = p.Amount, BalanceAfter = wallet.Balance });
                p.Status = "Succeeded"; p.CompletedAt = now; p.NextQueryAt = null;
                Emit(p.TransactionId, 1, new BookingPaymentResultEvent(p.TransactionId, p.BookingId!.Value, p.UpgradeId, p.Amount, p.Currency, true));
            }
        }
        if (provider == "VNPay") p.CheckoutUrl = gateway.Checkout(p, ip);
        db.Transactions.Add(p);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return p;
    }
    public async Task<string> SettleAsync(string provider, Settlement result, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync($"reference:{provider}:{result.Reference}", ct);
        var p = await db.Transactions.SingleOrDefaultAsync(x => x.Provider == provider && x.MerchantReference == result.Reference, ct);
        if (p is null) return "01";
        if (p.Amount != result.Amount || p.Currency != "VND") return "04";
        await LockAsync($"payment:{p.TransactionId}", ct);
        if (p.Status == "Succeeded" || p.Status == "Failed" && !result.Succeeded) return "02";
        if (result.Succeeded)
        {
            await LockAsync($"provider-id:{provider}:{result.ProviderId}", ct);
            if (await db.Transactions.AnyAsync(x => x.Provider == provider && x.ProviderTransactionId == result.ProviderId && x.TransactionId != p.TransactionId, ct)) return "99";
        }
        var eventKey = result.Reference + ":" + (result.Succeeded ? "success" : "failure");
        db.Webhooks.Add(new PaymentWebhook { Provider = provider, ProviderEventKey = eventKey, TransactionId = p.TransactionId,
            PayloadHash = result.Hash, Status = "Processed", ProcessedAt = DateTime.UtcNow });
        p.Status = result.Succeeded ? "Succeeded" : "Failed"; p.CompletedAt = DateTime.UtcNow;
        p.NextQueryAt = result.Succeeded ? null : DateTime.UtcNow.AddMinutes(5);
        if (result.Succeeded) p.ProviderTransactionId = result.ProviderId;
        if (p.Purpose == "Deposit" && result.Succeeded)
        {
            // A confirmed deposit remains owed to its owner even if their wallet is subsequently frozen.
            var wallet = await WalletAsync(p.UserId, ct);
            if (wallet.WalletId != p.WalletId || wallet.Currency != p.Currency) throw Conflict("wallet_mismatch");
            wallet.Balance += p.Amount; wallet.UpdatedAt = DateTime.UtcNow;
            db.Ledgers.Add(new WalletLedger { WalletId = wallet.WalletId, TransactionId = p.TransactionId,
                EntryType = "Deposit", Amount = p.Amount, BalanceAfter = wallet.Balance });
        }
        if (p.BookingId is Guid booking)
            Emit(p.TransactionId, result.Succeeded ? 2 : 1, new BookingPaymentResultEvent(p.TransactionId, booking, p.UpgradeId, p.Amount, p.Currency, result.Succeeded));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return "00";
    }
    // Called inside a transaction by the trusted Booking consumer or authenticated refund route.
    public async Task<PaymentRefund> RefundCoreAsync(Guid id, Guid actor, string key, string reason, bool trusted, CancellationToken ct)
    {
        await LockAsync($"payment:{id}", ct);
        var p = await db.Transactions.FindAsync([id], ct) ?? throw Missing();
        if (!trusted && p.UserId != actor) throw Missing();
        if (p.Purpose == "Deposit") throw Conflict("deposit_refund_requires_support");
        var existing = await db.Refunds.SingleOrDefaultAsync(x => x.TransactionId == id, ct);
        if (existing is not null) return existing; // full-refund invariant, even if a new request key arrives
        if (p.Status != "Succeeded") throw Conflict("payment_not_settled");
        if (!trusted)
        {
            var booking = await db.Bookings.FindAsync([p.BookingId!.Value], ct) ?? throw Missing();
            if (booking.Status is not ("Cancelled" or "Expired" or "RefundPending" or "Refunded")) throw Conflict("cancel_booking_first");
        }
        var refund = new PaymentRefund { TransactionId = id, RequestedBy = actor, IdempotencyKey = key, Amount = p.Amount, Reason = reason };
        db.Refunds.Add(refund);
        if (p.Provider == "Wallet")
        {
            var wallet = await WalletAsync(p.UserId, ct);
            if (wallet.WalletId != p.WalletId) throw Conflict("wallet_mismatch");
            wallet.Balance += refund.Amount; wallet.UpdatedAt = DateTime.UtcNow;
            db.Ledgers.Add(new WalletLedger { WalletId = wallet.WalletId, TransactionId = id, RefundId = refund.RefundId,
                EntryType = "Refund", Amount = refund.Amount, BalanceAfter = wallet.Balance });
            refund.Status = "Succeeded"; refund.CompletedAt = DateTime.UtcNow;
            Emit(refund.RefundId, 1, new BookingRefundResultEvent(id, p.BookingId!.Value, true));
        }
        return refund;
    }
    public async Task<PaymentRefund> RefundAsync(Guid user, RefundRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var refund = await RefundCoreAsync(request.TransactionId, user, request.IdempotencyKey, request.Reason, false, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return refund;
    }
    public static object View(PaymentTransaction p) => new { p.TransactionId, p.BookingId, p.UpgradeId, p.Purpose, p.Provider,
        p.Amount, p.Currency, p.Status, p.CheckoutUrl, p.ExpiresAt, p.CreatedAt, p.CompletedAt };
    public static object View(PaymentRefund r) => new { r.RefundId, r.TransactionId, r.Amount, r.Status, r.CreatedAt, r.CompletedAt,
        requires_reconciliation = r.LastError != null };
}
