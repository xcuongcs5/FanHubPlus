using FanHub.PaymentWalletService.Data;
using FanHub.PaymentWalletService.Services;
using FanHub.Shared.Contracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace FanHub.PaymentWalletService.Messaging;

public sealed class PaymentConsumer<T>(MessageHandler handler) : IConsumer<T> where T : class
{
    public Task Consume(ConsumeContext<T> context) => handler.ApplyAsync(context.MessageId ?? Guid.Empty, context.Message, context.CancellationToken);
}
public sealed class MessageHandler(PaymentDbContext db, PaymentService service)
{
    public async Task ApplyAsync<T>(Guid messageId, T message, CancellationToken ct) where T : class
    {
        if (messageId == Guid.Empty) throw new ArgumentException("MessageId required.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var consumer = typeof(T).Name;
        await service.LockAsync($"inbox:{consumer}:{messageId}", ct);
        if (await db.Inbox.AnyAsync(x => x.MessageId == messageId && x.Consumer == consumer, ct)) return;
        switch (message)
        {
            case BookingCreatedEvent m:
                await BookingAsync(m.BookingId, m.EventId, m.UserId, m.Amount, m.Currency, m.Status, m.ExpiresAt, m.SourceVersion, ct); break;
            case BookingChangedEvent m:
                await BookingAsync(m.BookingId, m.EventId, m.PurchaserId, m.Amount, m.Currency, m.Status, m.ExpiresAt, m.SourceVersion, ct); break;
            case UpgradeRequestedEvent m:
                await service.LockAsync($"booking:{m.BookingId}", ct);
                if (m.UpgradeId == Guid.Empty || m.UserId == Guid.Empty || m.Amount <= 0 || m.Currency != "VND") throw new ArgumentException("Invalid quote.");
                if (!await db.Bookings.AnyAsync(x => x.BookingId == m.BookingId && x.EventId == m.EventId, ct)) throw new InvalidOperationException("Booking projection unavailable.");
                var upgrade = await db.Upgrades.FindAsync([m.UpgradeId], ct);
                if (upgrade is null) db.Upgrades.Add(new UpgradeProjection { UpgradeId = m.UpgradeId, BookingId = m.BookingId,
                    UserId = m.UserId, Amount = m.Amount, Currency = m.Currency, Status = "Pending", ExpiresAt = m.ExpiresAt, SourceVersion = 1 });
                else if (upgrade.BookingId != m.BookingId || upgrade.UserId != m.UserId || upgrade.Amount != m.Amount || upgrade.ExpiresAt != m.ExpiresAt)
                    throw new ArgumentException("Conflicting immutable quote.");
                break;
            case BookingRefundRequestedEvent m:
                var payment = await db.Transactions.AsNoTracking().SingleOrDefaultAsync(x => x.TransactionId == m.TransactionId, ct)
                    ?? throw new InvalidOperationException("Payment unavailable.");
                if (payment.BookingId != m.BookingId || payment.Amount != m.Amount || payment.Currency != m.Currency || m.Reason.Length > 500)
                    throw new ArgumentException("Refund mismatch.");
                await service.RefundCoreAsync(m.TransactionId, payment.UserId, "booking:" + m.TransactionId.ToString("N"), m.Reason, true, ct); break;
            case UserCreatedEvent m: await UserAsync(m.UserId, m.FullName, m.AvatarUrl, m.CreatedAt, false, ct); break;
            case UserUpdatedEvent m: await UserAsync(m.UserId, m.FullName, m.AvatarUrl, m.UpdatedAt, false, ct); break;
            case UserBannedEvent m: await UserAsync(m.UserId, null, null, m.BannedAt, true, ct); break;
            default: throw new ArgumentException("Unsupported event.");
        }
        db.Inbox.Add(new InboxMessage { Consumer = consumer, MessageId = messageId });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private async Task BookingAsync(Guid id, Guid ev, Guid user, decimal amount, string currency, string status, DateTime expiry, long version, CancellationToken ct)
    {
        if (id == Guid.Empty || ev == Guid.Empty || user == Guid.Empty || version < 1 || amount < 0 || currency.Length != 3 || status.Length > 30 || expiry == default)
            throw new ArgumentException("Invalid booking snapshot.");
        await service.LockAsync($"booking:{id}", ct);
        var b = await db.Bookings.FindAsync([id], ct);
        if (b is not null && b.SourceVersion >= version) return;
        if (b is null) { b = new BookingProjection { BookingId = id }; db.Bookings.Add(b); }
        b.UserId = user; b.EventId = ev; b.Amount = amount; b.Currency = currency; b.Status = status;
        b.ExpiresAt = expiry; b.SourceVersion = version; b.SyncedAt = DateTime.UtcNow;
    }
    private async Task UserAsync(Guid id, string? name, string? avatar, DateTime timestamp, bool banned, CancellationToken ct)
    {
        if (id == Guid.Empty || timestamp == default || name?.Length > 200 || avatar?.Length > 500) throw new ArgumentException("Invalid user snapshot.");
        await service.LockAsync($"user:{id}", ct);
        var user = await db.Users.FindAsync([id], ct);
        if (user is not null && (user.SourceUpdatedAt > timestamp || user.SourceUpdatedAt == timestamp && !banned)) return;
        if (user is null) { user = new UserProjection { UserId = id }; db.Users.Add(user); }
        if (name is not null) { user.FullName = name; user.AvatarUrl = avatar; }
        if (banned) user.Status = "Banned";
        user.SourceUpdatedAt = timestamp; user.SyncedAt = DateTime.UtcNow;
    }
}
