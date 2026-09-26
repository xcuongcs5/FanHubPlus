using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FanHub.BookingService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nethereum.Signer;
using Nethereum.Util;

namespace FanHub.BookingService.Api;

[ApiController, Authorize, Route("api/v1/bookings")]
public sealed class BookingsController(BookingDbContext db, Services.BookingService bookings, IConfiguration configuration) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool Admin => User.IsInRole("Admin");
    private async Task<bool> OrganizerAsync(Guid id, CancellationToken ct) => Admin || await db.Events.AnyAsync(x => x.EventId == id && x.OrganizerId == UserId, ct);
    [HttpPost("reserve")]
    public async Task<IActionResult> Reserve(ReserveRequest input, CancellationToken ct)
    {
        var result = await bookings.ReserveAsync(UserId, input, ct);
        return Accepted($"/api/v1/bookings/status/{result.RequestId}", new { result.RequestId, result.Status });
    }
    [HttpGet("status/{requestId:guid}")]
    public async Task<IActionResult> Status(Guid requestId, CancellationToken ct)
    {
        var result = await db.Requests.AsNoTracking().SingleOrDefaultAsync(x => x.RequestId == requestId && x.UserId == UserId, ct);
        if (result is null) return NotFound();
        return Ok(new { result.RequestId, result.Status, result.FailureCode, result.CreatedAt, result.CompletedAt,
            tickets = await db.Tickets.AsNoTracking().Where(x => x.RequestId == requestId).Select(x => new { x.BookingId, x.Status, x.ExpiresAt }).ToListAsync(ct) });
    }
    [HttpGet("my-tickets")]
    public Task<object> MyTickets([FromQuery] PageQuery query, CancellationToken ct) => PageAsync(db.Tickets.Where(x => x.UserId == UserId), query, ct);
    private static async Task<object> PageAsync(IQueryable<TicketBooking> source, PageQuery query, CancellationToken ct) => new
    {
        total = await source.CountAsync(ct), query.Page, query.PageSize,
        items = await source.AsNoTracking().OrderByDescending(x => x.CreatedAt).ThenBy(x => x.BookingId)
            .Skip((int)Math.Min(int.MaxValue, ((long)query.Page - 1) * query.PageSize)).Take(query.PageSize)
            .Select(x => new { x.BookingId, x.EventId, x.TicketTypeId, x.UserId, x.UnitPrice, x.Currency, x.Status, x.ExpiresAt, x.NftTokenId, x.CheckedInAt }).ToListAsync(ct)
    };
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var ticket = await db.Tickets.AsNoTracking().SingleOrDefaultAsync(x => x.BookingId == id, ct);
        if (ticket is null || ticket.UserId != UserId && !await OrganizerAsync(ticket.EventId, ct)) return NotFound();
        return Ok(ticket);
    }
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => Ok(await bookings.CancelAsync(id, UserId, ct));
    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer(TransferRequest input, CancellationToken ct) => Accepted(await bookings.TransferAsync(UserId, input, ct));
    [HttpPost("{id:guid}/upgrade")]
    public async Task<IActionResult> Upgrade(Guid id, UpgradeRequest input, CancellationToken ct) => Accepted(await bookings.UpgradeAsync(id, UserId, input, ct));
    [HttpGet("event/{eventId:guid}")]
    public async Task<IActionResult> EventTickets(Guid eventId, [FromQuery] PageQuery query, CancellationToken ct)
    {
        if (!await OrganizerAsync(eventId, ct)) return Forbid();
        return Ok(await PageAsync(db.Tickets.Where(x => x.EventId == eventId &&
            (x.Status == "Paid" || x.Status == "MintPending" || x.Status == "Active" || x.Status == "TransferPending" || x.Status == "UpgradePending" || x.Status == "CheckedIn")), query, ct));
    }
    [HttpGet("ticket-types/{eventId:guid}")]
    public async Task<IActionResult> Types(Guid eventId, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == eventId, ct);
        if (ev is null || ev.Status != "Published" && !await OrganizerAsync(eventId, ct)) return NotFound();
        return Ok(await db.TicketTypes.AsNoTracking().Where(x => x.EventId == eventId && x.IsActive).OrderBy(x => x.TierRank)
            .Select(x => new { x.TicketTypeId, x.Name, x.Price, x.Currency, x.TierRank, x.SaleStart, x.SaleEnd,
                remaining = x.TotalQuantity - x.ReservedQuantity - x.SoldQuantity }).ToListAsync(ct));
    }
    [HttpPost("validate")]
    public async Task<IActionResult> Validate(ValidateRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ticket = await bookings.LockedTicketAsync(input.BookingId, ct);
        if (!await OrganizerAsync(ticket.EventId, ct) && !await db.Staffs.AnyAsync(x => x.EventId == ticket.EventId && x.UserId == UserId && x.IsActive && (x.Role == "CheckIn" || x.Role == "Manager"), ct))
            return Forbid();
        var ev = await db.Events.FindAsync([ticket.EventId], ct) ?? throw Services.BookingService.Missing();
        if (ticket.Status != "Active" || ev.Status != "Published" || DateTime.UtcNow < ev.StartTime.AddHours(-2) || DateTime.UtcNow >= ev.EndTime)
            throw Services.BookingService.Conflict("ticket_not_checkable");
        if (ticket.UserId != input.UserId || ticket.NftTokenId != input.NftTokenId) throw new ApiException(400, "qr_ticket_mismatch", "QR does not match the current ticket owner.");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (input.Timestamp < now - 30 || input.Timestamp > now + 5) throw new ApiException(400, "qr_expired", "QR timestamp is outside its validity window.");
        var signer = configuration["Blockchain:SignerAddress"];
        if (signer is null || !Regex.IsMatch(signer, "^0x[0-9a-fA-F]{40}$")) throw new ApiException(503, "qr_verifier_not_configured", "Blockchain signer address is not configured.");
        var bytes = Encoding.UTF8.GetBytes($"{input.NftTokenId}|{input.Timestamp}|{input.UserId:D}");
        string recovered;
        try { recovered = EthECKey.RecoverFromSignature(EthECDSASignatureFactory.ExtractECDSASignature(input.BlockchainSignature), new Sha3Keccack().CalculateHash(bytes)).GetPublicAddress(); }
        catch (Exception ex) when (ex is ArgumentException or FormatException or ArithmeticException or IndexOutOfRangeException)
        { throw new ApiException(400, "invalid_qr_signature", "QR signature is invalid."); }
        if (!string.Equals(signer, recovered, StringComparison.OrdinalIgnoreCase)) throw new ApiException(400, "invalid_qr_signature", "QR signature is invalid.");
        ticket.Status = "CheckedIn"; ticket.CheckedInAt = DateTime.UtcNow;
        db.CheckIns.Add(new TicketCheckIn { BookingId = ticket.BookingId, StaffUserId = UserId, QrPayloadHash = SHA256.HashData(bytes), CheckedInAt = ticket.CheckedInAt.Value });
        await bookings.SnapshotAsync(ticket, ct); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Ok(new { ticket.BookingId, ticket.Status, ticket.CheckedInAt });
    }
}
