using FanHub.BookingService.Data;
using FanHub.Shared.Contracts.Events;
using Microsoft.EntityFrameworkCore;

namespace FanHub.BookingService.Services;

public sealed partial class BookingService
{
    public async Task PaymentAsync(BookingPaymentResultEvent message, CancellationToken ct)
    {
        if (message.TransactionId == Guid.Empty || message.Amount <= 0 || message.Currency.Length != 3)
            throw new ArgumentException("Invalid payment result.");
        var ticket = await db.Tickets.FindAsync([message.BookingId], ct) ?? throw Missing();
        // A payment transaction remains idempotent even when its publisher generates a new MessageId.
        var previous = await db.Payments.FindAsync([message.TransactionId], ct);
        if (previous is not null)
        {
            if (previous.BookingId != message.BookingId || previous.UpgradeId != message.UpgradeId || previous.Amount != message.Amount || previous.Currency != message.Currency)
                throw new ArgumentException("Conflicting payment transaction.");
            return;
        }
        TicketUpgrade? upgrade = null;
        if (message.UpgradeId is not null)
        {
            upgrade = await db.Upgrades.FindAsync([message.UpgradeId.Value], ct) ?? throw Missing();
            if (upgrade.BookingId != ticket.BookingId) throw new ArgumentException("Upgrade does not belong to booking.");
        }
        if (!message.Succeeded)
        {
            if (upgrade?.Status == "Pending")
            { await EndUpgradeAsync(ticket, upgrade, "Failed", ct); await SnapshotAsync(ticket, ct); }
            return;
        }
        var payment = new BookingPayment { TransactionId = message.TransactionId, BookingId = ticket.BookingId,
            UpgradeId = message.UpgradeId, Amount = message.Amount, Currency = message.Currency, Status = "Applied" };
        db.Payments.Add(payment);
        var ev = await db.Events.FindAsync([ticket.EventId], ct) ?? throw Missing();
        var validEvent = ev.Status == "Published" && ev.StartTime > DateTime.UtcNow;
        if (upgrade is not null)
        {
            if (upgrade.Status == "Pending" && (upgrade.ExpiresAt <= DateTime.UtcNow || !validEvent))
            { await EndUpgradeAsync(ticket, upgrade, "Expired", ct); await SnapshotAsync(ticket, ct); }
            if (upgrade.Status != "Pending" || ticket.Status != "UpgradePending" || message.Amount != upgrade.PriceDifference || message.Currency != upgrade.Currency)
                Refund(payment, "upgrade_payment_not_applicable");
            else
            { await CompleteUpgradeAsync(ticket, upgrade, ct); await SnapshotAsync(ticket, ct); }
        }
        else
        {
            if (ticket.Status is "Reserved" or "PaymentPending" && (ticket.ExpiresAt <= DateTime.UtcNow || !validEvent))
                await ReleaseAsync(ticket, true, ct);
            if (ticket.Status is not ("Reserved" or "PaymentPending") || message.Amount != ticket.UnitPrice || message.Currency != ticket.Currency)
                Refund(payment, "booking_payment_not_applicable");
            else
            {
                var type = await db.TicketTypes.FindAsync([ticket.TicketTypeId], ct) ?? throw Missing();
                type.ReservedQuantity--; type.SoldQuantity++; ticket.Status = "MintPending"; ticket.PaidAt = DateTime.UtcNow;
                Emit(ticket.BookingId, 1, new TicketMintRequestedEvent(ticket.BookingId, ticket.EventId, ticket.UserId));
                await SnapshotAsync(ticket, ct);
            }
        }
    }
    public async Task RefundResultAsync(BookingRefundResultEvent message, CancellationToken ct)
    {
        var payment = await db.Payments.FindAsync([message.TransactionId], ct) ?? throw Missing();
        if (payment.BookingId != message.BookingId) throw new ArgumentException("Refund booking mismatch.");
        if (payment.Status is not ("RefundPending" or "RefundFailed")) return;
        payment.Status = message.Succeeded ? "Refunded" : "RefundFailed";
        await db.SaveChangesAsync(ct);
        var ticket = await db.Tickets.FindAsync([message.BookingId], ct) ?? throw Missing();
        if (ticket.Status == "RefundPending" && !await db.Payments.AnyAsync(x => x.BookingId == ticket.BookingId && x.Status != "Refunded", ct))
        { ticket.Status = "Refunded"; await SnapshotAsync(ticket, ct); }
    }
    public async Task MintAsync(TicketMintResultEvent message, CancellationToken ct)
    {
        if (message.NftTokenId?.Length > 200 || message.TransactionHash?.Length > 100 || message.Error?.Length > 1000 ||
            (message.Succeeded && (string.IsNullOrWhiteSpace(message.NftTokenId) || string.IsNullOrWhiteSpace(message.TransactionHash))))
            throw new ArgumentException("Invalid mint result.");
        var ticket = await db.Tickets.FindAsync([message.BookingId], ct) ?? throw Missing();
        if (ticket.Status != "MintPending") return;
        var error = message.Succeeded ? null : message.Error ?? "Mint failed; requires retry.";
        // A repeated failure with a new message ID must not emit the same rowversion twice.
        if (!message.Succeeded && ticket.BlockchainError == error) return;
        ticket.BlockchainError = error;
        if (message.Succeeded)
        { ticket.NftTokenId = message.NftTokenId; ticket.BlockchainTxHash = message.TransactionHash; ticket.Status = "Active"; }
        await SnapshotAsync(ticket, ct);
    }
    public async Task TransferResultAsync(TicketTransferResultEvent message, CancellationToken ct)
    {
        if (message.TransactionHash?.Length > 100 || message.Error?.Length > 1000 || (message.Succeeded && string.IsNullOrWhiteSpace(message.TransactionHash)))
            throw new ArgumentException("Invalid transfer result.");
        var transfer = await db.Transfers.FindAsync([message.TransferId], ct) ?? throw Missing();
        if (transfer.BookingId != message.BookingId) throw new ArgumentException("Transfer booking mismatch.");
        if (transfer.Status != "Pending") return;
        var ticket = await db.Tickets.FindAsync([message.BookingId], ct) ?? throw Missing();
        if (ticket.Status != "TransferPending" || ticket.UserId != transfer.FromUserId) throw Conflict("transfer_state_conflict");
        transfer.Status = message.Succeeded ? "Completed" : "Failed";
        transfer.CompletedAt = DateTime.UtcNow; transfer.BlockchainTxHash = message.TransactionHash;
        if (message.Succeeded) { ticket.UserId = transfer.ToUserId; ticket.BlockchainTxHash = message.TransactionHash; }
        ticket.Status = "Active"; ticket.BlockchainError = message.Succeeded ? null : message.Error;
        await SnapshotAsync(ticket, ct);
    }
    public async Task SweepAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var ids = await db.Tickets.AsNoTracking().Where(x =>
            ((x.Status == "Reserved" || x.Status == "PaymentPending") && x.ExpiresAt <= now) ||
            (x.Status == "UpgradePending" && db.Upgrades.Any(u => u.BookingId == x.BookingId && u.Status == "Pending" && u.ExpiresAt <= now)) ||
            ((x.Status == "Reserved" || x.Status == "PaymentPending" || x.Status == "MintPending" || x.Status == "Active" || x.Status == "Paid" || x.Status == "UpgradePending") &&
                db.Events.Any(e => e.EventId == x.EventId && e.Status == "Cancelled")))
            .OrderBy(x => x.CreatedAt).Select(x => x.BookingId).Take(100).ToListAsync(ct);
        foreach (var id in ids)
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var ticket = await LockedTicketAsync(id, ct);
            var ev = await db.Events.FindAsync([ticket.EventId], ct) ?? throw Missing();
            var cancelled = ev.Status == "Cancelled";
            if (ticket.Status == "UpgradePending")
            {
                var upgrade = await db.Upgrades.SingleAsync(x => x.BookingId == id && x.Status == "Pending", ct);
                if (cancelled || upgrade.ExpiresAt <= DateTime.UtcNow)
                { await EndUpgradeAsync(ticket, upgrade, "Expired", ct); await SnapshotAsync(ticket, ct); }
            }
            if (cancelled || (ticket.Status is "Reserved" or "PaymentPending" && ticket.ExpiresAt <= DateTime.UtcNow))
                await ReleaseAsync(ticket, !cancelled, ct);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
    }
}
