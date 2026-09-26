using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FanHub.BookingService.Data;
using FanHub.Shared.Contracts.Events;
using Microsoft.EntityFrameworkCore;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Util;
using Xunit;

namespace FanHub.BookingService.IntegrationTests;

public sealed class BookingTests(BookingFixture fixture) : IClassFixture<BookingFixture>
{
    private async Task<EventChangedEvent> EventAsync(int quantity = 10, decimal price = 100)
    {
        var ev = new EventChangedEvent(Guid.NewGuid(), Guid.NewGuid(), "Booking integration", "Test", null,
            DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(3), 1000, "Published", false, Guid.NewGuid(), "Hall", "Address", 1, 1, [], 1);
        await fixture.PublishAsync(ev, nameof(EventChangedEvent));
        await TypeAsync(ev, quantity, price, 0);
        return ev;
    }
    private async Task<Guid> TypeAsync(EventChangedEvent ev, int quantity, decimal price, int rank)
    {
        var id = Guid.NewGuid();
        await fixture.PublishAsync(new TicketTypeConfiguredEvent(id, ev.EventId, "Tier " + rank, price, "VND", rank, quantity,
            DateTime.UtcNow.AddDays(-1), ev.StartTime.AddMinutes(-1), true, 1), nameof(TicketTypeConfiguredEvent));
        return id;
    }
    private async Task<Guid> BaseTypeAsync(Guid ev)
    { await using var db = fixture.Db(); return await db.TicketTypes.Where(x => x.EventId == ev && x.TierRank == 0).Select(x => x.TicketTypeId).SingleAsync(); }
    private async Task<Guid> ReserveAsync(HttpClient client, Guid ev, Guid type, string? key = null, int quantity = 1)
    {
        var response = await client.PostAsJsonAsync("/api/v1/bookings/reserve", new { event_id = ev, ticket_type_id = type, quantity, idempotency_key = key ?? Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("request_id").GetGuid();
        await BookingFixture.EventuallyAsync(async () => { await using var db = fixture.Db(); return await db.Requests.AnyAsync(x => x.RequestId == id && x.Status != "Pending"); });
        return id;
    }
    private async Task<TicketBooking> TicketAsync(Guid request)
    { await using var db = fixture.Db(); return await db.Tickets.AsNoTracking().SingleAsync(x => x.RequestId == request); }
    private async Task<TicketBooking> ActiveAsync(HttpClient client, EventChangedEvent ev, bool retryMint = false)
    {
        var ticket = await TicketAsync(await ReserveAsync(client, ev.EventId, await BaseTypeAsync(ev.EventId)));
        if (ticket.UnitPrice > 0) await fixture.PublishAsync(new BookingPaymentResultEvent(Guid.NewGuid(), ticket.BookingId, null, ticket.UnitPrice, ticket.Currency, true), nameof(BookingPaymentResultEvent));
        if (retryMint)
        {
            var failed = new TicketMintResultEvent(ticket.BookingId, false, null, null, "Temporary chain failure");
            await fixture.PublishAsync(failed, nameof(TicketMintResultEvent));
            await fixture.PublishAsync(failed, nameof(TicketMintResultEvent));
            Assert.Equal("MintPending", (await TicketAsync(ticket.RequestId)).Status);
        }
        await fixture.PublishAsync(new TicketMintResultEvent(ticket.BookingId, true, Guid.NewGuid().ToString("N"), "0xMint", null), nameof(TicketMintResultEvent));
        return await TicketAsync(ticket.RequestId);
    }
    [Fact]
    public async Task Reservation_is_async_idempotent_and_does_not_oversell()
    {
        var ev = await EventAsync(3); var type = await BaseTypeAsync(ev.EventId); var user = Guid.NewGuid();
        using var client = fixture.Client(user);
        var requests = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => ReserveAsync(client, ev.EventId, type, "stock-" + i)));
        await using var db = fixture.Db();
        Assert.Equal(3, await db.Tickets.CountAsync(x => x.EventId == ev.EventId));
        Assert.Equal(5, await db.Requests.CountAsync(x => x.EventId == ev.EventId && x.FailureCode == "sold_out"));
        var stock = await db.TicketTypes.SingleAsync(x => x.TicketTypeId == type);
        Assert.Equal(3, stock.ReservedQuantity); Assert.Equal(0, stock.SoldQuantity);
        Assert.Equal(requests[0], await ReserveAsync(client, ev.EventId, type, "stock-0"));
        var conflict = await client.PostAsJsonAsync("/api/v1/bookings/reserve", new { event_id = ev.EventId, ticket_type_id = type, quantity = 2, idempotency_key = "stock-0" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var request = requests.First(x => db.Requests.Single(r => r.RequestId == x).Status == "Completed");
        var ticket = await TicketAsync(request);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/bookings/status/{request}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/bookings/{ticket.BookingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/bookings/my-tickets")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/bookings/ticket-types/{ev.EventId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/bookings/event/{ev.EventId}")).StatusCode);
        using var organizer = fixture.Client(ev.OrganizerId, "EventOwner");
        Assert.Equal(HttpStatusCode.OK, (await organizer.GetAsync($"/api/v1/bookings/event/{ev.EventId}")).StatusCode);
        await BookingFixture.EventuallyAsync(() => Task.FromResult(fixture.Published.Any(x => x.Message.BookingId == ticket.BookingId && x.Message.Status == "Reserved")));
    }
    [Fact]
    public async Task Payment_mint_cancel_refund_are_idempotent()
    {
        var ev = await EventAsync(); using var client = fixture.Client(Guid.NewGuid());
        var ticket = await ActiveAsync(client, ev, retryMint: true); Assert.Equal("Active", ticket.Status);
        await using var db = fixture.Db();
        var payment = await db.Payments.SingleAsync(x => x.BookingId == ticket.BookingId);
        await fixture.PublishAsync(new BookingPaymentResultEvent(payment.TransactionId, ticket.BookingId, null, 100, "VND", true), nameof(BookingPaymentResultEvent));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/bookings/{ticket.BookingId}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/bookings/{ticket.BookingId}/cancel", null)).StatusCode);
        Assert.Equal("RefundPending", (await TicketAsync(ticket.RequestId)).Status);
        await fixture.PublishAsync(new BookingRefundResultEvent(payment.TransactionId, ticket.BookingId, false), nameof(BookingRefundResultEvent));
        Assert.Equal("RefundPending", (await TicketAsync(ticket.RequestId)).Status);
        await fixture.PublishAsync(new BookingRefundResultEvent(payment.TransactionId, ticket.BookingId, true), nameof(BookingRefundResultEvent));
        Assert.Equal("Refunded", (await TicketAsync(ticket.RequestId)).Status);
        var type = await db.TicketTypes.AsNoTracking().SingleAsync(x => x.TicketTypeId == ticket.TicketTypeId);
        Assert.Equal(0, type.ReservedQuantity + type.SoldQuantity);
        Assert.Equal(1, await db.Outbox.CountAsync(x => x.AggregateId == payment.TransactionId && x.EventType == nameof(BookingRefundRequestedEvent)));
    }
    [Fact]
    public async Task Expired_reservations_and_late_payments_cannot_reactivate_inventory()
    {
        var ev = await EventAsync(1); using var client = fixture.Client(Guid.NewGuid());
        var ticket = await TicketAsync(await ReserveAsync(client, ev.EventId, await BaseTypeAsync(ev.EventId)));
        await fixture.AdminSqlAsync($"UPDATE dbo.TICKET_BOOKING SET created_at=DATEADD(minute,-20,SYSUTCDATETIME()), expires_at=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE booking_id='{ticket.BookingId:D}'");
        await BookingFixture.EventuallyAsync(async () => (await TicketAsync(ticket.RequestId)).Status == "Expired");
        var transaction = Guid.NewGuid();
        await fixture.PublishAsync(new BookingPaymentResultEvent(transaction, ticket.BookingId, null, 100, "VND", true), nameof(BookingPaymentResultEvent));
        Assert.Equal("Expired", (await TicketAsync(ticket.RequestId)).Status);
        await using var db = fixture.Db();
        Assert.Equal("RefundPending", (await db.Payments.FindAsync(transaction))!.Status);
        Assert.Equal(0, (await db.TicketTypes.FindAsync(ticket.TicketTypeId))!.ReservedQuantity);
        Assert.Equal("Reserved", (await TicketAsync(await ReserveAsync(client, ev.EventId, ticket.TicketTypeId))).Status);
    }
    [Fact]
    public async Task Transfer_changes_owner_only_after_blockchain_confirmation()
    {
        var ev = await EventAsync(); var owner = Guid.NewGuid(); var recipient = Guid.NewGuid();
        using var client = fixture.Client(owner); using var receiver = fixture.Client(recipient);
        await fixture.PublishAsync(new UserCreatedEvent { UserId = recipient, FullName = "Recipient", CreatedAt = DateTime.UtcNow }, nameof(UserCreatedEvent));
        var ticket = await ActiveAsync(client, ev);
        var body = new { booking_id = ticket.BookingId, to_user_id = recipient, idempotency_key = "transfer-1" };
        var response = await client.PostAsJsonAsync("/api/v1/bookings/transfer", body);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var transfer = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transfer_id").GetGuid();
        Assert.Equal(owner, (await TicketAsync(ticket.RequestId)).UserId);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/bookings/{ticket.BookingId}/cancel", null)).StatusCode);
        await fixture.PublishAsync(new TicketTransferResultEvent(transfer, ticket.BookingId, true, "0xTransfer", null), nameof(TicketTransferResultEvent));
        Assert.Equal(recipient, (await TicketAsync(ticket.RequestId)).UserId);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/bookings/transfer", body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/bookings/{ticket.BookingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await receiver.GetAsync($"/api/v1/bookings/{ticket.BookingId}")).StatusCode);
    }
    [Fact]
    public async Task Upgrade_swaps_stock_after_payment_and_expiry_restores_original_ticket()
    {
        var ev = await EventAsync(); var upper = await TypeAsync(ev, 1, 150, 1); var top = await TypeAsync(ev, 1, 200, 2);
        using var client = fixture.Client(Guid.NewGuid()); var ticket = await ActiveAsync(client, ev);
        var response = await client.PostAsJsonAsync($"/api/v1/bookings/{ticket.BookingId}/upgrade", new { ticket_type_id = upper, idempotency_key = "upgrade-1" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var upgrade = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("upgrade_id").GetGuid();
        await fixture.PublishAsync(new BookingPaymentResultEvent(Guid.NewGuid(), ticket.BookingId, upgrade, 50, "VND", true), nameof(BookingPaymentResultEvent));
        var updated = await TicketAsync(ticket.RequestId); Assert.Equal(upper, updated.TicketTypeId); Assert.Equal(150, updated.UnitPrice);
        response = await client.PostAsJsonAsync($"/api/v1/bookings/{ticket.BookingId}/upgrade", new { ticket_type_id = top, idempotency_key = "upgrade-2" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var expired = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("upgrade_id").GetGuid();
        await fixture.AdminSqlAsync($"UPDATE dbo.TICKET_UPGRADE SET created_at=DATEADD(minute,-20,SYSUTCDATETIME()), expires_at=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE upgrade_id='{expired:D}'");
        await BookingFixture.EventuallyAsync(async () => (await TicketAsync(ticket.RequestId)).Status == "Active");
        var late = Guid.NewGuid();
        await fixture.PublishAsync(new BookingPaymentResultEvent(late, ticket.BookingId, expired, 50, "VND", true), nameof(BookingPaymentResultEvent));
        Assert.Equal(upper, (await TicketAsync(ticket.RequestId)).TicketTypeId);
        await using var db = fixture.Db();
        Assert.Equal("RefundPending", (await db.Payments.FindAsync(late))!.Status);
        Assert.Equal(0, (await db.TicketTypes.FindAsync(top))!.ReservedQuantity);
        Assert.Equal(0, (await db.TicketTypes.FindAsync(ticket.TicketTypeId))!.SoldQuantity);
        Assert.Equal(1, (await db.TicketTypes.FindAsync(upper))!.SoldQuantity);
    }
    [Fact]
    public async Task Checkin_requires_staff_valid_signature_fresh_QR_and_runs_once()
    {
        var ev = await EventAsync(2, 0); var owner = Guid.NewGuid(); var staff = Guid.NewGuid();
        using var client = fixture.Client(owner); using var scanner = fixture.Client(staff);
        var ticket = await ActiveAsync(client, ev);
        await fixture.PublishAsync(new EventStaffChangedEvent(ev.EventId, staff, "CheckIn", true, 1), nameof(EventStaffChangedEvent));
        object Qr(long timestamp, Nethereum.Signer.EthECKey key)
        {
            var hash = new Sha3Keccack().CalculateHash(Encoding.UTF8.GetBytes($"{ticket.NftTokenId}|{timestamp}|{owner:D}"));
            var sig = key.SignAndCalculateV(hash);
            var hex = "0x" + sig.R.ToHex().PadLeft(64, '0') + sig.S.ToHex().PadLeft(64, '0') + sig.V.ToHex();
            return new { booking_id = ticket.BookingId, nft_token_id = ticket.NftTokenId, user_id = owner, timestamp, blockchain_signature = hex };
        }
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/bookings/validate", Qr(now, fixture.QrKey))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scanner.PostAsJsonAsync("/api/v1/bookings/validate", Qr(now - 60, fixture.QrKey))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await scanner.PostAsJsonAsync("/api/v1/bookings/validate", Qr(now, Nethereum.Signer.EthECKey.GenerateKey()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scanner.PostAsJsonAsync("/api/v1/bookings/validate", Qr(now, fixture.QrKey))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await scanner.PostAsJsonAsync("/api/v1/bookings/validate", Qr(now, fixture.QrKey))).StatusCode);
        await using var db = fixture.Db(); Assert.Equal(1, await db.CheckIns.CountAsync(x => x.BookingId == ticket.BookingId));
        Assert.Equal("CheckedIn", (await TicketAsync(ticket.RequestId)).Status);
    }
    [Fact]
    public async Task Auth_validation_and_projection_order_are_enforced()
    {
        var user = Guid.NewGuid(); using var anonymous = fixture.Client(); using var client = fixture.Client(user);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/bookings/my-tickets")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/bookings/my-tickets?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/bookings/reserve", new { quantity = 0, idempotency_key = "x" })).StatusCode);
        await fixture.PublishAsync(new UserBannedEvent { UserId = user, BannedAt = DateTime.UtcNow }, nameof(UserBannedEvent));
        await fixture.PublishAsync(new UserUpdatedEvent { UserId = user, FullName = "Still banned", UpdatedAt = DateTime.UtcNow.AddSeconds(1) }, nameof(UserUpdatedEvent));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/bookings/my-tickets")).StatusCode);
        var ev = await EventAsync();
        await fixture.PublishAsync(ev with { SourceVersion = 3, Title = "Newest" }, nameof(EventChangedEvent));
        await fixture.PublishAsync(ev with { SourceVersion = 2 }, nameof(EventChangedEvent));
        await using var db = fixture.Db(); Assert.Equal("Newest", (await db.Events.FindAsync(ev.EventId))!.Title);
    }
    [Fact]
    public async Task Capacity_updates_and_outbox_failure_preserve_stock_and_ticket_state()
    {
        var ev = await EventAsync(); using var client = fixture.Client(Guid.NewGuid());
        var type = await BaseTypeAsync(ev.EventId);
        var ticket = await TicketAsync(await ReserveAsync(client, ev.EventId, type));
        await fixture.PublishAsync(ev with { Capacity = 1, SourceVersion = 2 }, nameof(EventChangedEvent));
        var refused = await ReserveAsync(client, ev.EventId, type);
        await using var db = fixture.Db();
        Assert.Equal("sold_out", (await db.Requests.FindAsync(refused))!.FailureCode);
        // Fail only this ticket's outbox INSERT, leaving the other fixture traffic unaffected.
        await fixture.AdminSqlAsync($"ALTER TABLE dbo.OUTBOX_MESSAGE WITH NOCHECK ADD CONSTRAINT test_booking_outbox_failure CHECK(aggregate_id <> '{ticket.BookingId:D}')");
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsync($"/api/v1/bookings/{ticket.BookingId}/cancel", null)).StatusCode);
            Assert.Equal("Reserved", (await TicketAsync(ticket.RequestId)).Status);
            Assert.Equal(1, (await db.TicketTypes.AsNoTracking().SingleAsync(x => x.TicketTypeId == type)).ReservedQuantity);
        }
        finally { await fixture.AdminSqlAsync("ALTER TABLE dbo.OUTBOX_MESSAGE DROP CONSTRAINT test_booking_outbox_failure"); }
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/bookings/{ticket.BookingId}/cancel", null)).StatusCode);
    }
    [Fact]
    public async Task Cancelled_event_releases_holds_and_refunds_paid_tickets()
    {
        var ev = await EventAsync(); using var client = fixture.Client(Guid.NewGuid());
        var active = await ActiveAsync(client, ev);
        var held = await TicketAsync(await ReserveAsync(client, ev.EventId, active.TicketTypeId));
        await fixture.PublishAsync(ev with { Status = "Cancelled", SourceVersion = 2 }, nameof(EventChangedEvent));
        await BookingFixture.EventuallyAsync(async () => (await TicketAsync(active.RequestId)).Status == "RefundPending" && (await TicketAsync(held.RequestId)).Status == "Cancelled");
        await using var db = fixture.Db(); var type = await db.TicketTypes.FindAsync(active.TicketTypeId);
        Assert.Equal(0, type!.SoldQuantity + type.ReservedQuantity);
    }
}
