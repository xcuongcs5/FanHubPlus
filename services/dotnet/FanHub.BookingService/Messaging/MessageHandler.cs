using FanHub.BookingService.Data;
using FanHub.Shared.Contracts.Events;
using Microsoft.EntityFrameworkCore;

namespace FanHub.BookingService.Messaging;

public sealed class MessageHandler(BookingDbContext db, Services.BookingService bookings)
{
    public async Task ApplyAsync<T>(Guid messageId, T message, CancellationToken ct) where T : class
    {
        if (messageId == Guid.Empty) throw new ArgumentException("MessageId is required.");
        var consumer = typeof(T).Name;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await bookings.LockAsync($"inbox:{consumer}:{messageId}", ct);
        if (await db.Inbox.AnyAsync(x => x.Consumer == consumer && x.MessageId == messageId, ct)) return;
        var eventId = message switch
        {
            EventChangedEvent m => m.EventId, EventStaffChangedEvent m => m.EventId, TicketTypeConfiguredEvent m => m.EventId,
            ReservationRequestedEvent m => await db.Requests.Where(x => x.RequestId == m.RequestId).Select(x => (Guid?)x.EventId).SingleOrDefaultAsync(ct) ?? throw new InvalidOperationException("Request unavailable."),
            BookingPaymentResultEvent m => await EventForTicketAsync(m.BookingId, ct),
            BookingRefundResultEvent m => await EventForTicketAsync(m.BookingId, ct),
            TicketMintResultEvent m => await EventForTicketAsync(m.BookingId, ct),
            TicketTransferResultEvent m => await EventForTicketAsync(m.BookingId, ct),
            _ => Guid.Empty
        };
        if (message is BookingPaymentResultEvent payment) await bookings.LockAsync($"payment:{payment.TransactionId}", ct);
        if (eventId != Guid.Empty) await bookings.EventLockAsync(eventId, ct);
        switch (message)
        {
            case EventChangedEvent m: await EventAsync(m, ct); break;
            case EventStaffChangedEvent m: await StaffAsync(m, ct); break;
            case TicketTypeConfiguredEvent m: await TypeAsync(m, ct); break;
            case ReservationRequestedEvent m: await bookings.ProcessReservationAsync(m.RequestId, ct); break;
            case BookingPaymentResultEvent m: await bookings.PaymentAsync(m, ct); break;
            case BookingRefundResultEvent m: await bookings.RefundResultAsync(m, ct); break;
            case TicketMintResultEvent m: await bookings.MintAsync(m, ct); break;
            case TicketTransferResultEvent m: await bookings.TransferResultAsync(m, ct); break;
            case UserCreatedEvent m: await UserAsync(m.UserId, m.FullName, m.AvatarUrl, m.CreatedAt, false, ct); break;
            case UserUpdatedEvent m: await UserAsync(m.UserId, m.FullName, m.AvatarUrl, m.UpdatedAt, false, ct); break;
            case UserBannedEvent m: await UserAsync(m.UserId, null, null, m.BannedAt, true, ct); break;
            default: throw new ArgumentException("Unsupported event.");
        }
        db.Inbox.Add(new InboxMessage { Consumer = consumer, MessageId = messageId });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private async Task<Guid> EventForTicketAsync(Guid id, CancellationToken ct) =>
        await db.Tickets.Where(x => x.BookingId == id).Select(x => (Guid?)x.EventId).SingleOrDefaultAsync(ct) ?? throw new InvalidOperationException("Booking unavailable.");
    private async Task EventAsync(EventChangedEvent m, CancellationToken ct)
    {
        if (m.EventId == Guid.Empty || m.OrganizerId == Guid.Empty || m.SourceVersion < 1 || string.IsNullOrWhiteSpace(m.Title) || m.Title.Length > 250 || m.Capacity < 1 || m.EndTime <= m.StartTime ||
            m.Status is not ("Draft" or "PendingReview" or "Published" or "Rejected" or "Cancelled" or "Completed")) throw new ArgumentException("Invalid event snapshot.");
        var item = await db.Events.FindAsync([m.EventId], ct);
        if (item is not null && item.SourceVersion >= m.SourceVersion) return;
        if (item is null) { item = new EventProjection { EventId = m.EventId }; db.Events.Add(item); }
        item.OrganizerId = m.OrganizerId; item.Title = m.Title; item.StartTime = m.StartTime; item.EndTime = m.EndTime;
        item.Capacity = m.Capacity; item.Status = m.Status; item.SourceVersion = m.SourceVersion; item.SyncedAt = DateTime.UtcNow;
    }
    private async Task StaffAsync(EventStaffChangedEvent m, CancellationToken ct)
    {
        if (m.EventId == Guid.Empty || m.UserId == Guid.Empty || m.SourceVersion < 1 || m.Role is not ("CheckIn" or "Manager" or "Support")) throw new ArgumentException("Invalid staff snapshot.");
        if (!await db.Events.AnyAsync(x => x.EventId == m.EventId, ct)) throw new InvalidOperationException("Event projection unavailable.");
        var item = await db.Staffs.FindAsync([m.EventId, m.UserId], ct);
        if (item is not null && item.SourceVersion >= m.SourceVersion) return;
        if (item is null) { item = new StaffProjection { EventId = m.EventId, UserId = m.UserId }; db.Staffs.Add(item); }
        item.Role = m.Role; item.IsActive = m.IsActive; item.SourceVersion = m.SourceVersion; item.SyncedAt = DateTime.UtcNow;
    }
    private async Task TypeAsync(TicketTypeConfiguredEvent m, CancellationToken ct)
    {
        if (m.TicketTypeId == Guid.Empty || m.SourceVersion < 1 || string.IsNullOrWhiteSpace(m.Name) || m.Name.Length > 100 || m.Price < 0 ||
            decimal.Round(m.Price, 4) != m.Price || m.Price > 999999999999999m || !System.Text.RegularExpressions.Regex.IsMatch(m.Currency, "^[A-Z]{3}$") || m.TierRank < 0 || m.TotalQuantity < 0 || m.SaleEnd <= m.SaleStart)
            throw new ArgumentException("Invalid ticket type configuration.");
        var ev = await db.Events.FindAsync([m.EventId], ct) ?? throw new InvalidOperationException("Event projection unavailable.");
        var type = await db.TicketTypes.FindAsync([m.TicketTypeId], ct);
        if (type is not null && type.SourceVersion >= m.SourceVersion) return;
        if (type is not null && (type.EventId != m.EventId || type.ReservedQuantity + type.SoldQuantity > m.TotalQuantity ||
            (type.ReservedQuantity + type.SoldQuantity > 0 && (type.Price != m.Price || type.Currency != m.Currency || type.TierRank != m.TierRank))))
            throw new ArgumentException("Ticket type configuration conflicts with existing inventory.");
        var allocated = await db.TicketTypes.Where(x => x.EventId == m.EventId && x.TicketTypeId != m.TicketTypeId).SumAsync(x => (long)x.TotalQuantity, ct);
        if (allocated + m.TotalQuantity > ev.Capacity || m.SaleEnd > ev.StartTime) throw new ArgumentException("Ticket allocation exceeds event capacity or sale window.");
        if (type is null) { type = new TicketType { TicketTypeId = m.TicketTypeId, EventId = m.EventId }; db.TicketTypes.Add(type); }
        type.Name = m.Name; type.Price = m.Price; type.Currency = m.Currency; type.TierRank = m.TierRank; type.TotalQuantity = m.TotalQuantity;
        type.SaleStart = m.SaleStart; type.SaleEnd = m.SaleEnd; type.IsActive = m.IsActive; type.SourceVersion = m.SourceVersion;
    }
    private async Task UserAsync(Guid id, string? name, string? avatar, DateTime timestamp, bool banned, CancellationToken ct)
    {
        if (id == Guid.Empty || timestamp == default || name?.Length > 200 || avatar?.Length > 500) throw new ArgumentException("Invalid user snapshot.");
        await bookings.LockAsync($"user:{id}", ct);
        var item = await db.Users.FindAsync([id], ct);
        if (item is not null && (item.SourceUpdatedAt > timestamp || item.SourceUpdatedAt == timestamp && !banned)) return;
        if (item is null) { item = new UserProjection { UserId = id, Status = "Active" }; db.Users.Add(item); }
        if (name is not null) { item.FullName = name; item.AvatarUrl = avatar; }
        if (banned) item.Status = "Banned";
        item.SourceUpdatedAt = timestamp; item.SyncedAt = DateTime.UtcNow;
    }
}
