using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FanHub.NotificationService.Push;
using FanHub.Shared.Contracts.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MassTransit;
using Xunit;

namespace FanHub.NotificationService.IntegrationTests;

public sealed class FakePushSender : IPushSender
{
    public ConcurrentQueue<(string Token, Guid Notification)> Sent { get; } = new();
    public Task<PushResult> SendAsync(string token, Guid notificationId, CancellationToken ct)
    {
        Sent.Enqueue((token, notificationId));
        return Task.FromResult(token.StartsWith("invalid-") ? new PushResult("InvalidToken", Error: "Unregistered") :
            token.StartsWith("retry-") ? new PushResult("Retry", Error: "Unavailable") : new PushResult("Sent", "projects/test/messages/" + notificationId));
    }
}
public sealed class NotificationTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    private Task PublishAsync(Guid user, Guid? id = null) => fixture.PublishAsync(new NotificationRequestedEvent(user, "Test", "Private title", "Private content"), nameof(NotificationRequestedEvent), id);
    private DeliveryWorker Worker() => new(fixture.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<DeliveryWorker>.Instance);
    private static object Device(Guid id, string token, bool active = true) => new { provider = "FCM", device_id = id, token, is_active = active };
    private static async Task<Guid> RegisterAsync(HttpClient client, Guid device, string token)
    {
        var result = await client.PostAsJsonAsync("/api/v1/notifications/device-token", Device(device, token));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var json = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.TryGetProperty("token", out _));
        return json.GetProperty("device_token_id").GetGuid();
    }
    [Fact]
    public async Task List_read_and_read_all_are_private_paginated_and_idempotent()
    {
        var user = Guid.NewGuid(); var other = Guid.NewGuid(); using var client = fixture.Client(user); using var outsider = fixture.Client(other);
        await PublishAsync(user); await PublishAsync(user); await PublishAsync(other);
        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications?page=1&page_size=1&unread_only=true");
        Assert.Equal(2, page.GetProperty("total").GetInt32()); Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        var id = page.GetProperty("items")[0].GetProperty("notification_id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PutAsync($"/api/v1/notifications/{id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/v1/notifications/{id}/read", null)).StatusCode);
        await using var db = fixture.Db(); var original = (await db.Notifications.AsNoTracking().SingleAsync(x => x.NotificationId == id)).ReadAt;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/v1/notifications/{id}/read", null)).StatusCode);
        Assert.Equal(original, (await db.Notifications.AsNoTracking().SingleAsync(x => x.NotificationId == id)).ReadAt);
        var all = await client.PutAsync("/api/v1/notifications/read-all", null); Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        Assert.Equal(1, (await all.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("updated_count").GetInt32());
        Assert.Equal(1, await db.Notifications.CountAsync(x => x.UserId == other && x.ReadAt == null));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/notifications?page_size=101")).StatusCode);
        using var anonymous = fixture.Client(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/notifications")).StatusCode);
    }
    [Fact]
    public async Task Inbox_deduplicates_and_booking_versions_reject_stale_notifications()
    {
        var user = Guid.NewGuid(); var id = Guid.NewGuid(); await PublishAsync(user, id); await PublishAsync(user, id);
        var booking = new BookingChangedEvent(Guid.NewGuid(), Guid.NewGuid(), user, user, 100, "VND", DateTime.UtcNow.AddMinutes(15), "Active", 2);
        await fixture.PublishAsync(booking, nameof(BookingChangedEvent));
        await fixture.PublishAsync(booking with { SourceVersion = 1, Status = "Expired" }, nameof(BookingChangedEvent));
        await fixture.PublishAsync(booking with { SourceVersion = 3 }, nameof(BookingChangedEvent));
        await using var db = fixture.Db(); Assert.Equal(2, await db.Notifications.CountAsync(x => x.UserId == user));
        Assert.Equal(3, (await db.BookingStates.FindAsync(booking.BookingId))!.SourceVersion);
    }
    [Fact]
    public async Task Rotation_logout_and_account_switch_cancel_old_deliveries()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var device = Guid.NewGuid(); var token = "token-" + Guid.NewGuid().ToString("N");
        using var a = fixture.Client(first); using var b = fixture.Client(second);
        var registration = await RegisterAsync(a, device, token); await PublishAsync(first);
        Assert.Equal(registration, await RegisterAsync(b, device, token));
        await using var db = fixture.Db();
        Assert.Equal("Skipped", (await db.Deliveries.SingleAsync(x => x.DeviceTokenId == registration)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsJsonAsync("/api/v1/notifications/device-token", Device(device, token, false))).StatusCode);
        await PublishAsync(second);
        Assert.Equal(HttpStatusCode.OK, (await b.PostAsJsonAsync("/api/v1/notifications/device-token", Device(device, token, false))).StatusCode);
        Assert.Equal(2, await db.Deliveries.CountAsync(x => x.DeviceTokenId == registration && x.Status == "Skipped"));
        var rotated = "token-new-" + Guid.NewGuid().ToString("N"); await RegisterAsync(b, device, rotated);
        Assert.Equal(HttpStatusCode.Conflict, (await b.PostAsJsonAsync("/api/v1/notifications/device-token", Device(Guid.NewGuid(), rotated))).StatusCode);
        await PublishAsync(second); await Worker().RunOneAsync(default);
        Assert.Contains(fixture.Sender.Sent, x => x.Token == rotated);
        Assert.DoesNotContain(fixture.Sender.Sent, x => x.Token == token);
    }
    [Fact]
    public async Task Concurrent_workers_claim_once_and_reclaim_abandoned_leases()
    {
        var user = Guid.NewGuid(); using var client = fixture.Client(user); var token = "claim-" + Guid.NewGuid().ToString("N");
        var device = await RegisterAsync(client, Guid.NewGuid(), token); await PublishAsync(user);
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Worker().RunOneAsync(default)));
        Assert.Single(fixture.Sender.Sent, x => x.Token == token);
        await using var db = fixture.Db(); var sent = await db.Deliveries.SingleAsync(x => x.DeviceTokenId == device);
        Assert.Equal("Sent", sent.Status); Assert.NotNull(sent.ProviderMessageId);
        await PublishAsync(user);
        await fixture.AdminSqlAsync($"UPDATE dbo.NOTIFICATION_DELIVERY SET status='Processing',lock_id=NEWID(),locked_until=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE device_token_id='{device}' AND status='Pending'");
        await Worker().RunOneAsync(default); Assert.Equal(2, fixture.Sender.Sent.Count(x => x.Token == token));
    }
    [Fact]
    public async Task Invalid_token_is_disabled_and_transient_failures_back_off()
    {
        var user = Guid.NewGuid(); using var client = fixture.Client(user);
        var invalid = await RegisterAsync(client, Guid.NewGuid(), "invalid-" + Guid.NewGuid().ToString("N"));
        var retry = await RegisterAsync(client, Guid.NewGuid(), "retry-" + Guid.NewGuid().ToString("N"));
        await PublishAsync(user); await Worker().RunOneAsync(default); await Worker().RunOneAsync(default);
        await using var db = fixture.Db();
        Assert.False((await db.Tokens.FindAsync(invalid))!.IsActive);
        var item = await db.Deliveries.SingleAsync(x => x.DeviceTokenId == retry);
        Assert.Equal("Pending", item.Status); Assert.Equal(1, item.AttemptCount); Assert.True(item.NextAttemptAt > DateTime.UtcNow.AddSeconds(30));
        await fixture.AdminSqlAsync($"UPDATE dbo.NOTIFICATION_DELIVERY SET attempt_count=12,next_attempt_at=SYSUTCDATETIME() WHERE delivery_id='{item.DeliveryId}'");
        await Worker().RunOneAsync(default);
        Assert.Equal("Failed", (await db.Deliveries.AsNoTracking().SingleAsync(x => x.DeliveryId == item.DeliveryId)).Status);
    }
    [Fact]
    public async Task Consumer_rolls_back_notification_and_inbox_when_delivery_insert_fails()
    {
        var user = Guid.NewGuid(); using var client = fixture.Client(user);
        var device = await RegisterAsync(client, Guid.NewGuid(), "rollback-" + Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid(); var message = new NotificationRequestedEvent(user, "Rollback", "Title", "Message");
        await fixture.AdminSqlAsync($"ALTER TABLE dbo.NOTIFICATION_DELIVERY WITH NOCHECK ADD CONSTRAINT test_delivery_failure CHECK(device_token_id <> '{device}')");
        try
        {
            await fixture.Bus.Publish(message, context => context.MessageId = id);
            await fixture.WaitForFaultAsync();
            await using var db = fixture.Db();
            Assert.False(await db.Notifications.AnyAsync(x => x.SourceMessageId == id));
            Assert.False(await db.Inbox.AnyAsync(x => x.MessageId == id));
        }
        finally { await fixture.AdminSqlAsync("ALTER TABLE dbo.NOTIFICATION_DELIVERY DROP CONSTRAINT test_delivery_failure"); }
        await fixture.PublishAsync(message, nameof(NotificationRequestedEvent), id);
        await Worker().RunOneAsync(default);
        await using var verified = fixture.Db();
        Assert.Equal(1, await verified.Notifications.CountAsync(x => x.SourceMessageId == id));
        Assert.Equal("Sent", (await verified.Deliveries.SingleAsync(x => x.DeviceTokenId == device)).Status);
    }
    [Fact]
    public async Task Concurrent_registration_is_unique_and_device_limit_is_enforced()
    {
        var user = Guid.NewGuid(); using var client = fixture.Client(user); var device = Guid.NewGuid(); var token = "same-" + Guid.NewGuid().ToString("N");
        var ids = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RegisterAsync(client, device, token)));
        Assert.Single(ids.Distinct());
        for (var i = 1; i < 20; i++) await RegisterAsync(client, Guid.NewGuid(), "limit-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/notifications/device-token", Device(Guid.NewGuid(), "limit-extra-" + Guid.NewGuid().ToString("N")))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/notifications/device-token", Device(device, token, false))).StatusCode);
        await RegisterAsync(client, Guid.NewGuid(), "limit-after-logout-" + Guid.NewGuid().ToString("N"));
    }
    [Fact]
    public async Task Banned_users_and_expired_delivery_are_not_sent()
    {
        var user = Guid.NewGuid(); using var client = fixture.Client(user); var token = "banned-" + Guid.NewGuid().ToString("N");
        var device = await RegisterAsync(client, Guid.NewGuid(), token); await PublishAsync(user);
        await fixture.PublishAsync(new UserBannedEvent { UserId = user, BannedAt = DateTime.UtcNow }, nameof(UserBannedEvent));
        await Worker().RunOneAsync(default);
        Assert.DoesNotContain(fixture.Sender.Sent, x => x.Token == token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/notifications")).StatusCode);
        await fixture.PublishAsync(new UserUpdatedEvent { UserId = user, FullName = "Updated", UpdatedAt = DateTime.UtcNow.AddSeconds(1) }, nameof(UserUpdatedEvent));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/notifications")).StatusCode);
        await using var db = fixture.Db(); Assert.Equal("Skipped", (await db.Deliveries.SingleAsync(x => x.DeviceTokenId == device)).Status);
        var another = Guid.NewGuid(); using var other = fixture.Client(another); var otherDevice = await RegisterAsync(other, Guid.NewGuid(), "expiry-" + Guid.NewGuid().ToString("N"));
        await PublishAsync(another);
        await fixture.AdminSqlAsync($"UPDATE dbo.NOTIFICATION_DELIVERY SET expires_at=DATEADD(second,-1,SYSUTCDATETIME()) WHERE device_token_id='{otherDevice}'");
        await Worker().RunOneAsync(default);
        Assert.Equal("Failed", (await db.Deliveries.SingleAsync(x => x.DeviceTokenId == otherDevice)).Status);
    }
}
