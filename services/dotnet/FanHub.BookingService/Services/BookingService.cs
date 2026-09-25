using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FanHub.BookingService.Api;
using FanHub.BookingService.Data;
using FanHub.Shared.Contracts.Events;
using Microsoft.EntityFrameworkCore;

namespace FanHub.BookingService.Services;

public sealed partial class BookingService(BookingDbContext db)
{
    public static ApiException Conflict(string code) => new(409, code, code.Replace('_', ' '));
    public static ApiException Missing() => new(404, "not_found", "Record not found.");
    public Task<int> LockAsync(string resource, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
        IF @result < 0 THROW 51000, 'Booking lock timeout', 1;
        """, ct);
    public Task<int> EventLockAsync(Guid id, CancellationToken ct) => LockAsync($"booking-event:{id}", ct);
    public void Emit<T>(Guid aggregate, long version, T message, Guid? correlation = null) where T : notnull =>
        db.Outbox.Add(new OutboxMessage { AggregateId = aggregate, AggregateVersion = version, EventType = typeof(T).Name,
            CorrelationId = correlation, Payload = JsonSerializer.Serialize(message) });
    public async Task SnapshotAsync(TicketBooking ticket, CancellationToken ct, bool created = false)
    {
        await db.SaveChangesAsync(ct);
        var version = BinaryPrimitives.ReadInt64BigEndian(ticket.Version);
        Emit(ticket.BookingId, version, new BookingChangedEvent(ticket.BookingId, ticket.EventId, ticket.UserId,
            ticket.PurchaserId, ticket.UnitPrice, ticket.Currency, ticket.ExpiresAt, ticket.Status, version), ticket.RequestId);
        Emit(ticket.BookingId, version, new BookingAttendeeChangedEvent(ticket.BookingId, ticket.EventId,
            ticket.UserId, ticket.Status, ticket.CheckedInAt, version), ticket.RequestId);
        if (created) Emit(ticket.BookingId, version, new BookingCreatedEvent(ticket.BookingId, ticket.RequestId,
            ticket.EventId, ticket.UserId, ticket.UnitPrice, ticket.Currency, ticket.ExpiresAt, ticket.Status, version), ticket.RequestId);
    }
    private async Task<EventProjection> SellableAsync(Guid eventId, CancellationToken ct)
    {
        var item = await db.Events.FindAsync([eventId], ct) ?? throw Missing();
        if (item.Status != "Published" || item.StartTime <= DateTime.UtcNow) throw Conflict("event_not_on_sale");
        return item;
    }
    private static bool OnSale(TicketType type) => type.IsActive && type.SaleStart <= DateTime.UtcNow && type.SaleEnd > DateTime.UtcNow;
    public async Task<BookingRequest> ReserveAsync(Guid user, ReserveRequest input, CancellationToken ct)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{input.EventId:D}|{input.TicketTypeId:D}|{input.Quantity}"));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync($"reserve:{user}:{input.IdempotencyKey}", ct);
        var existing = await db.Requests.SingleOrDefaultAsync(x => x.UserId == user && x.IdempotencyKey == input.IdempotencyKey, ct);
        if (existing is not null)
        {
            if (!existing.RequestHash.SequenceEqual(hash)) throw Conflict("idempotency_key_reused");
            return existing;
        }
        await EventLockAsync(input.EventId, ct);
        await SellableAsync(input.EventId, ct);
        var type = await db.TicketTypes.SingleOrDefaultAsync(x => x.EventId == input.EventId && x.TicketTypeId == input.TicketTypeId, ct) ?? throw Missing();
        if (!OnSale(type)) throw Conflict("ticket_type_not_on_sale");
        var request = new BookingRequest { UserId = user, EventId = input.EventId, TicketTypeId = input.TicketTypeId,
            Quantity = input.Quantity, IdempotencyKey = input.IdempotencyKey, RequestHash = hash, Status = "Pending" };
        db.Requests.Add(request);
        Emit(request.RequestId, 1, new ReservationRequestedEvent(request.RequestId), request.RequestId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return request;
    }
    // Called within the inbox transaction and event lock.
    public async Task ProcessReservationAsync(Guid id, CancellationToken ct)
    {
        var request = await db.Requests.FindAsync([id], ct) ?? throw new InvalidOperationException("Reservation request has not arrived.");
        if (request.Status != "Pending") return;
        var ev = await db.Events.FindAsync([request.EventId], ct);
        var type = await db.TicketTypes.FindAsync([request.TicketTypeId], ct);
        // Event capacity can shrink after ticket types have been configured.
        var committed = await db.Tickets.LongCountAsync(x => x.EventId == request.EventId &&
            (x.Status == "Reserved" || x.Status == "PaymentPending" || x.Status == "Paid" || x.Status == "MintPending" ||
             x.Status == "Active" || x.Status == "TransferPending" || x.Status == "UpgradePending" || x.Status == "CheckedIn"), ct);
        var failure = request.CreatedAt.AddMinutes(15) <= DateTime.UtcNow ? "request_expired"
            : await db.Users.AnyAsync(x => x.UserId == request.UserId && x.Status != "Active", ct) ? "account_inactive"
            : ev is null || ev.Status != "Published" || ev.StartTime <= DateTime.UtcNow ? "event_not_on_sale"
            : type is null || !OnSale(type) ? "ticket_type_not_on_sale"
            : type.TotalQuantity - type.ReservedQuantity - type.SoldQuantity < request.Quantity || committed + request.Quantity > ev.Capacity ? "sold_out" : null;
        request.CompletedAt = DateTime.UtcNow;
        if (failure is not null)
        {
            request.Status = failure == "request_expired" ? "Expired" : "Failed"; request.FailureCode = failure;
            return;
        }
        request.Status = "Completed";
        type!.ReservedQuantity += request.Quantity;
        for (var number = 1; number <= request.Quantity; number++)
        {
            var ticket = new TicketBooking { RequestId = id, TicketNumber = number, EventId = request.EventId,
                TicketTypeId = request.TicketTypeId, PurchaserId = request.UserId, UserId = request.UserId,
                UnitPrice = type.Price, Currency = type.Currency, Status = "Reserved", ExpiresAt = DateTime.UtcNow.AddMinutes(15) };
            if (type.Price == 0)
            {
                type.ReservedQuantity--; type.SoldQuantity++; ticket.Status = "MintPending";
                Emit(ticket.BookingId, 1, new TicketMintRequestedEvent(ticket.BookingId, ticket.EventId, ticket.UserId));
            }
            db.Tickets.Add(ticket);
            await SnapshotAsync(ticket, ct, true);
        }
    }
    public async Task<TicketBooking> LockedTicketAsync(Guid id, CancellationToken ct)
    {
        var eventId = await db.Tickets.Where(x => x.BookingId == id).Select(x => (Guid?)x.EventId).SingleOrDefaultAsync(ct) ?? throw Missing();
        await EventLockAsync(eventId, ct);
        return await db.Tickets.SingleAsync(x => x.BookingId == id, ct);
    }
    public async Task<TicketBooking> CancelAsync(Guid id, Guid user, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ticket = await LockedTicketAsync(id, ct);
        if (ticket.UserId != user) throw Missing();
        if (ticket.Status is "Cancelled" or "Expired" or "RefundPending" or "Refunded") return ticket;
        var ev = await db.Events.FindAsync([ticket.EventId], ct) ?? throw Missing();
        if (ev.StartTime <= DateTime.UtcNow) throw Conflict("event_already_started");
        if (ticket.Status is not ("Reserved" or "PaymentPending" or "Active" or "MintPending" or "Paid")) throw Conflict("ticket_cannot_be_cancelled");
        await ReleaseAsync(ticket, false, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return ticket;
    }
    public async Task ReleaseAsync(TicketBooking ticket, bool expired, CancellationToken ct)
    {
        var type = await db.TicketTypes.FindAsync([ticket.TicketTypeId], ct) ?? throw Missing();
        if (ticket.Status is "Reserved" or "PaymentPending") type.ReservedQuantity--;
        else if (ticket.Status is "Active" or "MintPending" or "Paid") type.SoldQuantity--;
        else return;
        var payments = await db.Payments.Where(x => x.BookingId == ticket.BookingId && x.Status == "Applied").ToListAsync(ct);
        foreach (var payment in payments) Refund(payment, "booking_cancelled");
        ticket.Status = payments.Count > 0 ? "RefundPending" : expired ? "Expired" : "Cancelled";
        ticket.CancelledAt = DateTime.UtcNow;
        await SnapshotAsync(ticket, ct);
    }
    private void Refund(BookingPayment payment, string reason)
    {
        payment.Status = "RefundPending"; payment.RefundReason = reason;
        Emit(payment.TransactionId, 1, new BookingRefundRequestedEvent(payment.TransactionId, payment.BookingId, payment.Amount, payment.Currency, reason));
    }
    public async Task<TicketTransfer> TransferAsync(Guid user, TransferRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync($"transfer:{user}:{input.IdempotencyKey}", ct);
        var existing = await db.Transfers.SingleOrDefaultAsync(x => x.FromUserId == user && x.IdempotencyKey == input.IdempotencyKey, ct);
        if (existing is not null)
        {
            if (existing.BookingId != input.BookingId || existing.ToUserId != input.ToUserId) throw Conflict("idempotency_key_reused");
            return existing;
        }
        var ticket = await LockedTicketAsync(input.BookingId, ct);
        if (ticket.UserId != user) throw Missing();
        await SellableAsync(ticket.EventId, ct);
        if (ticket.Status != "Active" || ticket.NftTokenId is null) throw Conflict("ticket_not_transferable");
        if (input.ToUserId == user || !await db.Users.AnyAsync(x => x.UserId == input.ToUserId && x.Status == "Active", ct))
            throw Conflict("recipient_not_active");
        var transfer = new TicketTransfer { BookingId = ticket.BookingId, FromUserId = user, ToUserId = input.ToUserId,
            IdempotencyKey = input.IdempotencyKey, Status = "Pending" };
        db.Transfers.Add(transfer); ticket.Status = "TransferPending";
        Emit(transfer.TransferId, 1, new TicketTransferRequestedEvent(transfer.TransferId, ticket.BookingId, ticket.NftTokenId, user, input.ToUserId));
        await SnapshotAsync(ticket, ct); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return transfer;
    }
    public async Task<TicketUpgrade> UpgradeAsync(Guid id, Guid user, UpgradeRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ticket = await LockedTicketAsync(id, ct);
        if (ticket.UserId != user) throw Missing();
        var existing = await db.Upgrades.SingleOrDefaultAsync(x => x.BookingId == id && x.IdempotencyKey == input.IdempotencyKey, ct);
        if (existing is not null)
        {
            if (existing.ToTicketTypeId != input.TicketTypeId) throw Conflict("idempotency_key_reused");
            return existing;
        }
        await SellableAsync(ticket.EventId, ct);
        if (ticket.Status != "Active") throw Conflict("ticket_not_upgradeable");
        var from = await db.TicketTypes.FindAsync([ticket.TicketTypeId], ct) ?? throw Missing();
        var target = await db.TicketTypes.FindAsync([input.TicketTypeId], ct) ?? throw Missing();
        if (target.EventId != ticket.EventId || target.TierRank <= from.TierRank || target.Currency != ticket.Currency || target.Price < ticket.UnitPrice)
            throw Conflict("invalid_upgrade_target");
        if (!OnSale(target) || target.TotalQuantity <= target.ReservedQuantity + target.SoldQuantity) throw Conflict("upgrade_unavailable");
        var upgrade = new TicketUpgrade { BookingId = id, EventId = ticket.EventId, FromTicketTypeId = ticket.TicketTypeId,
            ToTicketTypeId = target.TicketTypeId, PriceDifference = target.Price - ticket.UnitPrice, Currency = ticket.Currency,
            IdempotencyKey = input.IdempotencyKey, Status = "Pending", ExpiresAt = DateTime.UtcNow.AddMinutes(15) };
        target.ReservedQuantity++; ticket.Status = "UpgradePending"; db.Upgrades.Add(upgrade);
        if (upgrade.PriceDifference == 0) await CompleteUpgradeAsync(ticket, upgrade, ct);
        else Emit(upgrade.UpgradeId, 1, new UpgradeRequestedEvent(upgrade.UpgradeId, id, ticket.EventId, user,
            upgrade.PriceDifference, upgrade.Currency, upgrade.ExpiresAt));
        await SnapshotAsync(ticket, ct); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return upgrade;
    }
    private async Task CompleteUpgradeAsync(TicketBooking ticket, TicketUpgrade upgrade, CancellationToken ct)
    {
        var from = await db.TicketTypes.FindAsync([upgrade.FromTicketTypeId], ct) ?? throw Missing();
        var target = await db.TicketTypes.FindAsync([upgrade.ToTicketTypeId], ct) ?? throw Missing();
        from.SoldQuantity--; target.ReservedQuantity--; target.SoldQuantity++;
        ticket.TicketTypeId = target.TicketTypeId; ticket.UnitPrice += upgrade.PriceDifference; ticket.Status = "Active";
        upgrade.Status = "Completed"; upgrade.CompletedAt = DateTime.UtcNow;
    }
    public async Task EndUpgradeAsync(TicketBooking ticket, TicketUpgrade upgrade, string status, CancellationToken ct)
    {
        if (upgrade.Status != "Pending") return;
        var target = await db.TicketTypes.FindAsync([upgrade.ToTicketTypeId], ct) ?? throw Missing();
        target.ReservedQuantity--; upgrade.Status = status; upgrade.CompletedAt = DateTime.UtcNow;
        ticket.Status = "Active";
    }
}
