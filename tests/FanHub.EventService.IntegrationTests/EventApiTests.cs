using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FanHub.EventService.Messaging;
using FanHub.Shared.Contracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FanHub.EventService.IntegrationTests;

public sealed class EventApiTests(EventFixture fixture) : IClassFixture<EventFixture>
{
    private static Dictionary<string, object?> Body(string? version = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = "FanHub test event", ["description"] = "Plain text description",
            ["start_time"] = DateTimeOffset.UtcNow.AddDays(7), ["end_time"] = DateTimeOffset.UtcNow.AddDays(7).AddHours(3),
            ["capacity"] = 100, ["location"] = new { name = "Test venue", address = "Test address", latitude = 10.7m, longitude = 106.7m }
        };
        if (version is not null) body["version"] = version;
        return body;
    }
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }
    private static async Task<Guid> Create(HttpClient client) => (await Json(await client.PostAsJsonAsync("/api/v1/events", Body()), HttpStatusCode.Created)).GetProperty("id").GetGuid();

    [Fact]
    public async Task All_12_routes_work_through_review_and_cancellation()
    {
        var ownerId = Guid.NewGuid();
        using var owner = fixture.Client(ownerId, "EventOwner");
        using var reader = fixture.Client(Guid.NewGuid());
        var id = await Create(owner);
        var detail = await Json(await owner.GetAsync($"/api/v1/events/{id}"));
        Assert.Equal("Draft", detail.GetProperty("status").GetString());
        Assert.EndsWith("Z", detail.GetProperty("start_time").GetString());
        var oldVersion = detail.GetProperty("version").GetString()!;
        await Json(await owner.PutAsJsonAsync($"/api/v1/events/{id}", Body(oldVersion)));
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync($"/api/v1/events/{id}", Body(oldVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/v1/events/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync($"/api/v1/events/{id}/publish", null)).StatusCode);

        var category = Guid.NewGuid();
        await fixture.PublishAsync(new CategoryChangedEvent(category, null, "Test category", false, 1), "CategoryProjection");
        var categories = await Json(await owner.GetAsync("/api/v1/categories?limit=100"));
        Assert.Contains(categories.GetProperty("data").EnumerateArray(), x => x.GetProperty("id").GetGuid() == category);
        await Json(await owner.PostAsJsonAsync($"/api/v1/events/{id}/categories", new { category_ids = new[] { category } }));
        await Json(await owner.PostAsJsonAsync($"/api/v1/events/{id}/categories", new { category_ids = new[] { category } }));
        var staff = Guid.NewGuid();
        await fixture.PublishAsync(new UserCreatedEvent { UserId = staff, FullName = "Staff", CreatedAt = DateTime.UtcNow }, "UserProjection");
        await Json(await owner.PostAsJsonAsync($"/api/v1/events/{id}/staffs", new { user_id = staff, role = "CheckIn" }));
        await Json(await owner.PostAsJsonAsync($"/api/v1/events/{id}/staffs", new { user_id = staff, role = "CheckIn" }));

        var pending = await Json(await owner.PostAsync($"/api/v1/events/{id}/publish", null), HttpStatusCode.Accepted);
        var requestId = pending.GetProperty("review_request_id").GetGuid();
        Assert.Equal("PendingReview", pending.GetProperty("status").GetString());
        var repeat = await Json(await owner.PostAsync($"/api/v1/events/{id}/publish", null), HttpStatusCode.Accepted);
        Assert.Equal(requestId, repeat.GetProperty("review_request_id").GetGuid());
        await fixture.PublishAsync(new EventReviewDecisionEvent(id, requestId, "AI", "Flagged", null, "Manual review required"), "EventReview");
        // Unrelated mutations must not bypass the requirement for human review.
        await Json(await owner.PostAsJsonAsync($"/api/v1/events/{id}/staffs", new { user_id = staff, role = "Manager" }));
        await fixture.PublishAsync(new EventReviewDecisionEvent(id, requestId, "AI", "Approved", null, null), "EventReview");
        Assert.Equal("PendingReview", (await Json(await owner.GetAsync($"/api/v1/events/{id}"))).GetProperty("status").GetString());
        await fixture.PublishAsync(new EventReviewDecisionEvent(id, requestId, "Admin", "Approved", Guid.NewGuid(), "Approved"), "EventReview");
        Assert.Equal("Published", (await Json(await reader.GetAsync($"/api/v1/events/{id}"))).GetProperty("status").GetString());

        var feed = await Json(await reader.GetAsync("/api/v1/events?limit=100&sort=start_time"));
        Assert.Contains(feed.GetProperty("data").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        var mine = await Json(await owner.GetAsync("/api/v1/events/organizer/me"));
        Assert.Contains(mine.GetProperty("data").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        // Featured selection is an admin responsibility outside tasks 21–32. Seed only in the isolated test DB.
        await using (var db = fixture.Db()) await db.Events.Where(x => x.EventId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsFeatured, true));
        var featured = await Json(await reader.GetAsync("/api/v1/events/featured?limit=100"));
        Assert.Contains(featured.GetProperty("data").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        var ticket = Guid.NewGuid();
        await fixture.PublishAsync(new BookingAttendeeChangedEvent(ticket, id, staff, "Active", null, 1), "AttendeeProjection");
        var attendees = await Json(await owner.GetAsync($"/api/v1/events/{id}/attendees"));
        Assert.Contains(attendees.GetProperty("data").EnumerateArray(), x => x.GetProperty("booking_id").GetGuid() == ticket);
        await Json(await owner.DeleteAsync($"/api/v1/events/{id}"));
        await Json(await owner.DeleteAsync($"/api/v1/events/{id}"));
        await fixture.PublishAsync(new EventReviewDecisionEvent(id, requestId, "Admin", "Approved", Guid.NewGuid(), null), "EventReview");
        Assert.Equal("Cancelled", (await Json(await owner.GetAsync($"/api/v1/events/{id}"))).GetProperty("status").GetString());
        await EventFixture.EventuallyAsync(() => Task.FromResult(fixture.Published.Any(x => x.Message.EventId == id && x.Message.Status == "Cancelled")));
        await using var verify = fixture.Db();
        Assert.Equal(1, await verify.Staffs.CountAsync(x => x.EventId == id));
        Assert.Equal(1, await verify.EventCategories.CountAsync(x => x.EventId == id));
        var row = await verify.Outbox.SingleAsync(x => x.AggregateId == id && x.EventType == nameof(EventSubmittedForReviewEvent));
        Assert.NotNull(row.PublishedAt);
        foreach (var observed in fixture.Published.Where(x => x.Message.EventId == id))
            Assert.True(await verify.Outbox.AnyAsync(x => x.MessageId == observed.MessageId));
    }

    [Fact]
    public async Task Jwt_and_ownership_are_enforced()
    {
        using var anonymous = fixture.Client();
        using var user = fixture.Client(Guid.NewGuid());
        using var owner = fixture.Client(Guid.NewGuid(), "EventOwner");
        using var stranger = fixture.Client(Guid.NewGuid(), "EventOwner");
        using var admin = fixture.Client(Guid.NewGuid(), "Admin");
        using var badKey = fixture.Client(Guid.NewGuid(), key: new string('x', 48));
        using var expired = fixture.Client(Guid.NewGuid(), expiry: DateTime.UtcNow.AddMinutes(-5));
        using var badSubject = fixture.Client(subject: "usr_not_a_guid");
        foreach (var client in new[] { anonymous, badKey, expired, badSubject })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/events")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/v1/events", Body())).StatusCode);
        var id = await Create(owner);
        var version = (await Json(await owner.GetAsync($"/api/v1/events/{id}"))).GetProperty("version").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PutAsJsonAsync($"/api/v1/events/{id}", Body(version))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.DeleteAsync($"/api/v1/events/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/v1/events/{id}/attendees")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsJsonAsync($"/api/v1/events/{id}/categories", new { category_ids = new[] { Guid.NewGuid() } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsJsonAsync($"/api/v1/events/{id}/staffs", new { user_id = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync($"/api/v1/events/{id}/publish", null)).StatusCode);
        await Json(await admin.GetAsync($"/api/v1/events/{id}"));
        await Json(await admin.DeleteAsync($"/api/v1/events/{id}"));
    }

    [Fact]
    public async Task Invalid_input_never_creates_events()
    {
        using var owner = fixture.Client(Guid.NewGuid(), "EventOwner");
        foreach (var field in new[] { "title", "description", "location", "start_time", "end_time" })
        {
            var body = Body(); body.Remove(field);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/events", body)).StatusCode);
        }
        var invalidBodies = new[] { Body(), Body(), Body(), Body(), Body(), Body() };
        invalidBodies[0]["title"] = "<script>alert(1)</script>";
        invalidBodies[1]["capacity"] = 0;
        invalidBodies[2]["end_time"] = DateTimeOffset.UtcNow;
        invalidBodies[3]["banner_url"] = "javascript:alert(1)";
        invalidBodies[4]["status"] = "Published";
        invalidBodies[5]["location"] = new { name = "Test", address = "Test", latitude = 100, longitude = 0 };
        foreach (var body in invalidBodies) Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/events", body)).StatusCode);
        foreach (var query in new[] { "page=0", "limit=101", "sort=invalid" })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/v1/events?" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/events/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_updates_with_same_version_have_exactly_one_winner()
    {
        using var owner = fixture.Client(Guid.NewGuid(), "EventOwner");
        var id = await Create(owner);
        var version = (await Json(await owner.GetAsync($"/api/v1/events/{id}"))).GetProperty("version").GetString();
        var results = await Task.WhenAll(owner.PutAsJsonAsync($"/api/v1/events/{id}", Body(version)), owner.PutAsJsonAsync($"/api/v1/events/{id}", Body(version)));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Rabbit_projections_ignore_duplicates_and_older_versions()
    {
        var category = Guid.NewGuid(); var messageId = Guid.NewGuid();
        var latest = new CategoryChangedEvent(category, null, "Latest", false, 2);
        await fixture.PublishAsync(latest, "CategoryProjection", messageId);
        await fixture.Bus.Publish(latest, context => context.MessageId = messageId);
        await fixture.PublishAsync(latest with { Name = "Old", SourceVersion = 1 }, "CategoryProjection");
        await using (var db = fixture.Db())
        {
            Assert.Equal("Latest", (await db.Categories.SingleAsync(x => x.CategoryId == category)).Name);
            Assert.Equal(1, await db.Inbox.CountAsync(x => x.Consumer == "CategoryProjection" && x.MessageId == messageId));
        }
        var user = Guid.NewGuid(); var timestamp = DateTime.UtcNow;
        await fixture.PublishAsync(new UserBannedEvent { UserId = user, BannedAt = timestamp }, "UserProjection");
        await fixture.PublishAsync(new UserCreatedEvent { UserId = user, FullName = "Old profile", CreatedAt = timestamp.AddMinutes(-1) }, "UserProjection");
        await fixture.PublishAsync(new UserUpdatedEvent { UserId = user, FullName = "New name", UpdatedAt = timestamp.AddMinutes(1) }, "UserProjection");
        using var banned = fixture.Client(user);
        Assert.Equal(HttpStatusCode.Unauthorized, (await banned.GetAsync("/api/v1/events")).StatusCode);
    }

    [Fact]
    public async Task Stale_moderation_cannot_approve_a_new_submission()
    {
        using var owner = fixture.Client(Guid.NewGuid(), "EventOwner");
        var id = await Create(owner); var category = Guid.NewGuid();
        await fixture.PublishAsync(new CategoryChangedEvent(category, null, "Review category", false, 1), "CategoryProjection");
        await Json(await owner.PostAsJsonAsync($"/api/v1/events/{id}/categories", new { category_ids = new[] { category } }));
        var oldRequest = (await Json(await owner.PostAsync($"/api/v1/events/{id}/publish", null), HttpStatusCode.Accepted)).GetProperty("review_request_id").GetGuid();
        await fixture.PublishAsync(new EventReviewDecisionEvent(id, oldRequest, "AI", "Rejected", null, "Revise"), "EventReview");
        var version = (await Json(await owner.GetAsync($"/api/v1/events/{id}"))).GetProperty("version").GetString();
        await Json(await owner.PutAsJsonAsync($"/api/v1/events/{id}", Body(version)));
        var newRequest = (await Json(await owner.PostAsync($"/api/v1/events/{id}/publish", null), HttpStatusCode.Accepted)).GetProperty("review_request_id").GetGuid();
        Assert.NotEqual(oldRequest, newRequest);
        await fixture.PublishAsync(new EventReviewDecisionEvent(id, oldRequest, "Admin", "Approved", Guid.NewGuid(), null), "EventReview");
        Assert.Equal("PendingReview", (await Json(await owner.GetAsync($"/api/v1/events/{id}"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Event_and_location_rollback_when_outbox_insert_fails()
    {
        var ownerId = Guid.NewGuid();
        using var owner = fixture.Client(ownerId, "EventOwner");
        var title = "Rollback-probe-" + Guid.NewGuid().ToString("N");
        var constraint = "CK_test_outbox_" + Guid.NewGuid().ToString("N");
        await using var beforeDb = fixture.Db();
        var beforeLocations = await beforeDb.Locations.CountAsync();
        await fixture.AdminSqlAsync($"ALTER TABLE dbo.OUTBOX_MESSAGE ADD CONSTRAINT [{constraint}] CHECK (JSON_VALUE(payload,'$.Title') <> N'{title}')");
        try
        {
            var body = Body(); body["title"] = title;
            Assert.Equal(HttpStatusCode.InternalServerError, (await owner.PostAsJsonAsync("/api/v1/events", body)).StatusCode);
            await using var db = fixture.Db();
            Assert.False(await db.Events.AnyAsync(x => x.OrganizerId == ownerId));
            Assert.False(await db.Outbox.AnyAsync(x => x.Payload.Contains(title)));
            Assert.Equal(beforeLocations, await db.Locations.CountAsync());
        }
        finally { await fixture.AdminSqlAsync($"ALTER TABLE dbo.OUTBOX_MESSAGE DROP CONSTRAINT [{constraint}]"); }
    }

    [Fact]
    public async Task Swagger_contains_all_assigned_paths()
    {
        using var client = fixture.Client();
        var swagger = await Json(await client.GetAsync("/swagger/v1/swagger.json"));
        var paths = swagger.GetProperty("paths");
        var operationCount = paths.EnumerateObject().Sum(path => path.Value.EnumerateObject().Count());
        Assert.Equal(12, operationCount);
    }
}
