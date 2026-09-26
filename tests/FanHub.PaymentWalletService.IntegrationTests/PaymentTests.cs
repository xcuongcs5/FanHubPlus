using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FanHub.PaymentWalletService.Data;
using FanHub.PaymentWalletService.Providers;
using FanHub.Shared.Contracts.Events;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FanHub.PaymentWalletService.IntegrationTests;

public sealed class PaymentTests(PaymentFixture fixture) : IClassFixture<PaymentFixture>
{
    private async Task<Guid> Quote(Guid user, decimal amount = 20000, string status = "Reserved", long version = 1, Guid? id = null)
    {
        var booking = id ?? Guid.NewGuid();
        await fixture.PublishAsync(new BookingCreatedEvent(booking, Guid.NewGuid(), Guid.NewGuid(), user, amount, "VND", DateTime.UtcNow.AddMinutes(10), status, version), nameof(BookingCreatedEvent));
        return booking;
    }
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }
    private async Task<PaymentTransaction> Intent(Guid user, Guid booking, string provider = "VNPay", string? key = null)
    {
        using var client = fixture.Client(user);
        var response = await Json(await client.PostAsJsonAsync("/api/v1/payments/create-intent", new { booking_id = booking, provider, idempotency_key = key ?? Guid.NewGuid().ToString("N") }));
        await using var db = fixture.Db(); return await db.Transactions.AsNoTracking().SingleAsync(x => x.TransactionId == response.GetProperty("transaction_id").GetGuid());
    }
    private static Dictionary<string, string> Fields(PaymentTransaction p, decimal? amount = null, bool success = true) => new()
    {
        ["vnp_TmnCode"] = "TESTCODE", ["vnp_TxnRef"] = p.MerchantReference, ["vnp_Amount"] = VnPay.Amount(amount ?? p.Amount),
        ["vnp_TransactionNo"] = Math.Abs(BitConverter.ToInt64(p.TransactionId.ToByteArray()) % 99999999999999L).ToString(),
        ["vnp_ResponseCode"] = success ? "00" : "24", ["vnp_TransactionStatus"] = success ? "00" : "02"
    };
    private async Task<string> Ipn(Dictionary<string, string> fields, bool post = false)
    {
        fields["vnp_SecureHash"] = VnPay.Sign(PaymentFixture.ProviderSecret, VnPay.Canonical(fields));
        using var client = fixture.Client();
        var response = post ? await client.PostAsync("/api/v1/payments/webhook/vnpay", new FormUrlEncodedContent(fields)) :
            await client.GetAsync("/api/v1/payments/webhook/vnpay?" + await new FormUrlEncodedContent(fields).ReadAsStringAsync());
        return (await Json(response)).GetProperty("RspCode").GetString()!;
    }
    private async Task<PaymentTransaction> Deposit(Guid user, decimal amount = 50000)
    {
        using var client = fixture.Client(user);
        var response = await Json(await client.PostAsJsonAsync("/api/v1/wallets/deposit", new { amount, provider = "VNPay", idempotency_key = Guid.NewGuid().ToString("N") }));
        await using var db = fixture.Db(); return await db.Transactions.AsNoTracking().SingleAsync(x => x.TransactionId == response.GetProperty("transaction_id").GetGuid());
    }
    [Fact]
    public async Task Intent_uses_trusted_quote_and_rejects_reused_keys_and_second_checkout()
    {
        var user = Guid.NewGuid(); var booking = await Quote(user); var key = Guid.NewGuid().ToString("N");
        var p = await Intent(user, booking, key: key); var retry = await Intent(user, booking, key: key);
        Assert.Equal(p.TransactionId, retry.TransactionId); Assert.Equal(20000, p.Amount);
        Assert.Contains("vnp_Amount=2000000", p.CheckoutUrl); Assert.DoesNotContain(PaymentFixture.ProviderSecret, p.CheckoutUrl);
        using var client = fixture.Client(user);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/payments/create-intent", new { booking_id = booking, provider = "Wallet", idempotency_key = key })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/payments/create-intent", new { booking_id = booking, provider = "VNPay", idempotency_key = "second" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/payments/create-intent", new { booking_id = booking, provider = "VNPay", idempotency_key = "third", amount = 1 })).StatusCode);
    }
    [Fact]
    public async Task Ipn_authenticates_signature_merchant_amount_and_is_concurrently_idempotent()
    {
        var user = Guid.NewGuid(); var p = await Intent(user, await Quote(user));
        using var client = fixture.Client();
        var bad = Fields(p); bad["vnp_SecureHash"] = new string('0', 128);
        Assert.Equal("97", (await Json(await client.PostAsync("/api/v1/payments/webhook/vnpay", new FormUrlEncodedContent(bad)))).GetProperty("RspCode").GetString());
        var merchant = Fields(p); merchant["vnp_TmnCode"] = "OTHERONE"; Assert.Equal("97", await Ipn(merchant));
        Assert.Equal("04", await Ipn(Fields(p, 1)));
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Ipn(Fields(p), i % 2 == 0)));
        Assert.Single(results, x => x == "00"); Assert.Equal(5, results.Count(x => x == "02"));
        await PaymentFixture.EventuallyAsync(() => Task.FromResult(fixture.Published.Any(x => x.Message.TransactionId == p.TransactionId)));
        await using var db = fixture.Db(); Assert.Equal(1, await db.Webhooks.CountAsync(x => x.TransactionId == p.TransactionId));
        Assert.Equal("Succeeded", (await db.Transactions.FindAsync(p.TransactionId))!.Status);
        Assert.Equal("02", await Ipn(Fields(p, success: false)));
    }
    [Fact]
    public async Task Return_url_never_settles_and_duplicate_query_keys_are_rejected()
    {
        var user = Guid.NewGuid(); var p = await Intent(user, await Quote(user)); var fields = Fields(p);
        fields["vnp_SecureHash"] = VnPay.Sign(PaymentFixture.ProviderSecret, VnPay.Canonical(fields));
        var query = await new FormUrlEncodedContent(fields).ReadAsStringAsync(); using var client = fixture.Client();
        (await client.GetAsync("/api/v1/payments/return/vnpay?" + query)).EnsureSuccessStatusCode();
        Assert.Equal("97", (await Json(await client.GetAsync("/api/v1/payments/webhook/vnpay?" + query + "&vnp_Amount=1"))).GetProperty("RspCode").GetString());
        await using var db = fixture.Db(); Assert.Equal("Pending", (await db.Transactions.FindAsync(p.TransactionId))!.Status);
    }
    [Fact]
    public async Task Deposit_credits_once_and_concurrent_wallet_payments_cannot_overdraw()
    {
        var user = Guid.NewGuid(); var deposit = await Deposit(user, 30000);
        await using (var db = fixture.Db()) Assert.Equal(0, (await db.Wallets.SingleAsync(x => x.UserId == user)).Balance);
        await Ipn(Fields(deposit)); await Ipn(Fields(deposit));
        var b1 = await Quote(user); var b2 = await Quote(user); using var client = fixture.Client(user);
        var responses = await Task.WhenAll(new[] { b1, b2 }.Select(b => client.PostAsJsonAsync("/api/v1/payments/create-intent", new { booking_id = b, provider = "Wallet", idempotency_key = b.ToString("N") })));
        Assert.Single(responses, x => x.IsSuccessStatusCode); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var final = fixture.Db(); var wallet = await final.Wallets.SingleAsync(x => x.UserId == user);
        Assert.Equal(10000, wallet.Balance); Assert.Equal(2, await final.Ledgers.CountAsync(x => x.WalletId == wallet.WalletId));
        var history = await Json(await client.GetAsync("/api/v1/wallets/transactions?page=1&page_size=1"));
        Assert.Equal(1, history.GetProperty("items").GetArrayLength()); Assert.Equal(2, history.GetProperty("total").GetInt32());
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => final.Database.ExecuteSqlRawAsync("UPDATE dbo.WALLET_LEDGER SET balance_after=0"));
    }
    [Fact]
    public async Task Refund_requires_cancellation_and_wallet_refund_is_exactly_once()
    {
        var user = Guid.NewGuid(); await Ipn(Fields(await Deposit(user))); var b = await Quote(user); var p = await Intent(user, b, "Wallet");
        using var client = fixture.Client(user);
        var request = new { transaction_id = p.TransactionId, idempotency_key = "refund-one", reason = "Cancelled ticket" };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/payments/refund", request)).StatusCode);
        await Quote(user, status: "RefundPending", version: 2, id: b);
        (await client.PostAsJsonAsync("/api/v1/payments/refund", request)).EnsureSuccessStatusCode();
        await fixture.PublishAsync(new BookingRefundRequestedEvent(p.TransactionId, b, p.Amount, p.Currency, "booking_cancelled"), nameof(BookingRefundRequestedEvent));
        await using var db = fixture.Db(); Assert.Equal(50000, (await db.Wallets.SingleAsync(x => x.UserId == user)).Balance);
        Assert.Equal(1, await db.Refunds.CountAsync(x => x.TransactionId == p.TransactionId));
        Assert.Equal(1, await db.Ledgers.CountAsync(x => x.TransactionId == p.TransactionId && x.EntryType == "Refund"));
    }
    [Fact]
    public async Task Late_settlement_is_published_for_booking_compensation_and_refund_is_durable()
    {
        var user = Guid.NewGuid(); var b = await Quote(user); var p = await Intent(user, b);
        await Quote(user, status: "Expired", version: 2, id: b);
        Assert.Equal("00", await Ipn(Fields(p)));
        await fixture.PublishAsync(new BookingRefundRequestedEvent(p.TransactionId, b, p.Amount, p.Currency, "booking_payment_not_applicable"), nameof(BookingRefundRequestedEvent));
        await using var db = fixture.Db(); var refund = await db.Refunds.SingleAsync(x => x.TransactionId == p.TransactionId);
        Assert.Equal("Pending", refund.Status); Assert.Null(refund.CompletedAt);
    }
    [Fact]
    public async Task Auth_ownership_inactive_users_and_invalid_amounts_are_enforced()
    {
        var user = Guid.NewGuid(); var b = await Quote(user); await Intent(user, b);
        using var anon = fixture.Client(); using var other = fixture.Client(Guid.NewGuid()); using var client = fixture.Client(user);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/wallets/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/payments/{b}/status")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/wallets/deposit", new { amount = 10000.5, provider = "VNPay", idempotency_key = "decimal" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/wallets/transactions?page_size=101")).StatusCode);
        await fixture.PublishAsync(new UserBannedEvent { UserId = user, Reason = "test", BannedAt = DateTime.UtcNow }, nameof(UserBannedEvent));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/wallets/me")).StatusCode);
    }
    [Fact]
    public async Task MoMo_verifies_camel_case_signature_and_never_accepts_authorization_as_capture()
    {
        var user = Guid.NewGuid(); var p = await Deposit(user);
        await using (var db = fixture.Db())
            await db.Transactions.Where(x => x.TransactionId == p.TransactionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Provider, "MoMo"));
        var fields = new Dictionary<string, object> { ["partnerCode"] = "TESTPARTNER", ["orderId"] = p.MerchantReference,
            ["requestId"] = p.MerchantReference, ["amount"] = 50000L, ["orderInfo"] = "Test", ["orderType"] = "momo_wallet",
            ["transId"] = 123456789L, ["resultCode"] = 9000, ["message"] = "OK", ["payType"] = "qr", ["responseTime"] = 123456789L, ["extraData"] = "" };
        void Sign()
        {
            var keys = "amount,extraData,message,orderId,orderInfo,orderType,partnerCode,payType,requestId,responseTime,resultCode,transId".Split(',');
            var raw = "accessKey=test-access&" + string.Join("&", keys.Select(k => k + "=" + fields[k]));
            fields["signature"] = Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData(Encoding.UTF8.GetBytes(PaymentFixture.ProviderSecret), Encoding.UTF8.GetBytes(raw)));
        }
        using var client = fixture.Client(); Sign();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/payments/webhook/momo", fields)).StatusCode);
        await using (var db = fixture.Db()) Assert.Equal("Pending", (await db.Transactions.FindAsync(p.TransactionId))!.Status);
        fields["resultCode"] = 0;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/payments/webhook/momo", fields)).StatusCode);
        Sign();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/payments/webhook/momo", fields)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/payments/webhook/momo", fields)).StatusCode);
        await using var final = fixture.Db(); Assert.Equal(50000, (await final.Wallets.SingleAsync(x => x.UserId == user)).Balance);
    }
    [Fact]
    public async Task Upgrade_uses_its_owner_and_quote_and_ignores_stale_booking_snapshots()
    {
        var purchaser = Guid.NewGuid(); var currentOwner = Guid.NewGuid(); var b = await Quote(purchaser, status: "UpgradePending", version: 10);
        Guid eventId;
        await using (var db = fixture.Db()) eventId = (await db.Bookings.FindAsync(b))!.EventId;
        var upgrade = Guid.NewGuid();
        await fixture.PublishAsync(new UpgradeRequestedEvent(upgrade, b, eventId, currentOwner, 10000, "VND", DateTime.UtcNow.AddMinutes(5)), nameof(UpgradeRequestedEvent));
        await Quote(purchaser, status: "Reserved", version: 1, id: b);
        using var client = fixture.Client(currentOwner);
        var response = await Json(await client.PostAsJsonAsync("/api/v1/payments/create-intent", new { booking_id = b, upgrade_id = upgrade, provider = "VNPay", idempotency_key = "upgrade" }));
        Assert.Equal(10000, response.GetProperty("amount").GetDecimal());
        Assert.Equal(upgrade, response.GetProperty("upgrade_id").GetGuid());
    }
    [Fact]
    public async Task Outbox_failure_rolls_back_settlement_and_wallet_credit()
    {
        var user = Guid.NewGuid(); var p = await Intent(user, await Quote(user));
        await fixture.AdminSqlAsync("EXEC(N'CREATE TRIGGER dbo.fail_payment_outbox ON dbo.OUTBOX_MESSAGE INSTEAD OF INSERT AS THROW 51001, ''test failure'', 1;');");
        try
        {
            var fields = Fields(p); fields["vnp_SecureHash"] = VnPay.Sign(PaymentFixture.ProviderSecret, VnPay.Canonical(fields));
            using var client = fixture.Client(); var response = await client.PostAsync("/api/v1/payments/webhook/vnpay", new FormUrlEncodedContent(fields));
            Assert.Equal("99", (await Json(response)).GetProperty("RspCode").GetString());
            await using var db = fixture.Db(); Assert.Equal("Pending", (await db.Transactions.FindAsync(p.TransactionId))!.Status);
            Assert.False(await db.Webhooks.AnyAsync(x => x.TransactionId == p.TransactionId));
        }
        finally { await fixture.AdminSqlAsync("DROP TRIGGER dbo.fail_payment_outbox"); }
        Assert.Equal("00", await Ipn(Fields(p)));
    }
}
