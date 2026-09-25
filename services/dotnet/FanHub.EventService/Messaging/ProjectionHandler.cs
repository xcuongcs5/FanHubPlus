using FanHub.EventService.Data;
using FanHub.Shared.Contracts.Events;
using Microsoft.EntityFrameworkCore;

namespace FanHub.EventService.Messaging;

public sealed class ProjectionHandler(EventDbContext db, Services.EventService events)
{
    // The inbox and projection mutation commit together. Messages for one aggregate serialize.
    private async Task ApplyAsync(Guid messageId, string consumer, string aggregate, Func<Task> apply, CancellationToken ct)
    {
        if (messageId == Guid.Empty) throw new ArgumentException("MessageId is required.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync($"inbox:{consumer}:{messageId}", ct);
        if (await db.Inbox.AnyAsync(x => x.Consumer == consumer && x.MessageId == messageId, ct)) return;
        await LockAsync(aggregate, ct);
        await apply();
        db.Inbox.Add(new InboxMessage { Consumer = consumer, MessageId = messageId });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private Task<int> LockAsync(string resource, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
        IF @result < 0 THROW 51000, 'Projection lock timeout', 1;
        """, ct);

    public Task CategoryAsync(Guid messageId, CategoryChangedEvent message, CancellationToken ct)
    {
        if (message.CategoryId == Guid.Empty || message.SourceVersion < 1 || string.IsNullOrWhiteSpace(message.Name) || message.Name.Length > 200)
            throw new ArgumentException("Invalid category snapshot.");
        return ApplyAsync(messageId, "CategoryProjection", $"category:{message.CategoryId}", async () =>
        {
            var item = await db.Categories.FindAsync([message.CategoryId], ct);
            if (item is not null && item.SourceVersion >= message.SourceVersion) return;
            if (item is null) { item = new CategoryProjection { CategoryId = message.CategoryId }; db.Categories.Add(item); }
            item.Name = message.Name; item.ParentId = message.ParentId; item.IsDeleted = message.IsDeleted;
            item.SourceVersion = message.SourceVersion; item.SyncedAt = DateTime.UtcNow;
        }, ct);
    }

    private static readonly string[] TicketStates = ["Reserved", "PaymentPending", "Paid", "MintPending", "Active", "TransferPending", "UpgradePending", "CheckedIn", "Cancelled", "Expired", "RefundPending", "Refunded"];
    public Task AttendeeAsync(Guid messageId, BookingAttendeeChangedEvent message, CancellationToken ct)
    {
        if (message.BookingId == Guid.Empty || message.EventId == Guid.Empty || message.UserId == Guid.Empty || message.SourceVersion < 1 || !TicketStates.Contains(message.Status))
            throw new ArgumentException("Invalid attendee snapshot.");
        return ApplyAsync(messageId, "AttendeeProjection", $"booking:{message.BookingId}", async () =>
        {
            var item = await db.Attendees.FindAsync([message.BookingId], ct);
            if (item is not null && item.SourceVersion >= message.SourceVersion) return;
            if (item is null) { item = new AttendeeProjection { BookingId = message.BookingId }; db.Attendees.Add(item); }
            item.EventId = message.EventId; item.UserId = message.UserId; item.Status = message.Status;
            item.CheckedInAt = message.CheckedInAt; item.SourceVersion = message.SourceVersion; item.SyncedAt = DateTime.UtcNow;
        }, ct);
    }

    public Task UserAsync(Guid messageId, Guid userId, string? name, string? avatar, DateTime timestamp, bool banned, CancellationToken ct)
    {
        if (userId == Guid.Empty || timestamp == default || name?.Length > 200 || avatar?.Length > 500)
            throw new ArgumentException("Invalid user snapshot.");
        return ApplyAsync(messageId, "UserProjection", $"user:{userId}", async () =>
        {
            var item = await db.Users.FindAsync([userId], ct);
            if (item is not null && (item.SourceUpdatedAt > timestamp || (item.SourceUpdatedAt == timestamp && !banned))) return;
            if (item is null) { item = new UserProjection { UserId = userId }; db.Users.Add(item); }
            if (name is not null) { item.FullName = name; item.AvatarUrl = avatar; }
            // Identity has no unban contract: a profile update must never implicitly unban.
            if (banned) item.Status = "Banned";
            item.SourceUpdatedAt = timestamp; item.SyncedAt = DateTime.UtcNow;
        }, ct);
    }

    public Task ReviewAsync(Guid messageId, EventReviewDecisionEvent message, CancellationToken ct)
    {
        if (message.EventId == Guid.Empty || message.ReviewRequestId == Guid.Empty || message.Source is not ("AI" or "Admin") ||
            message.Decision is not ("Approved" or "Rejected" or "Flagged") || message.Reason?.Length > 2000 ||
            (message.Source == "Admin" && (message.ReviewerId is null || message.ReviewerId == Guid.Empty)))
            throw new ArgumentException("Invalid moderation decision.");
        return ApplyAsync(messageId, "EventReview", $"review:{message.EventId}", async () =>
        {
            var item = await events.LockAsync(message.EventId, ct);
            if (item is null || item.ReviewRequestId != message.ReviewRequestId || item.Status != "PendingReview") return;
            if (message.Source == "AI" && await db.Reviews.AnyAsync(x => x.EventId == item.EventId &&
                x.Decision == "Flagged" && x.ReviewRequestId == message.ReviewRequestId, ct)) return;
            db.Reviews.Add(new EventReview { EventId = item.EventId, ReviewRequestId = message.ReviewRequestId, Source = message.Source,
                ReviewerId = message.ReviewerId, Decision = message.Decision, Reason = message.Reason });
            if (message.Decision == "Flagged") return;
            item.Status = message.Decision == "Approved" && item.StartTime > DateTime.UtcNow ? "Published" : "Rejected";
            if (item.Status == "Published") item.PublishedAt = DateTime.UtcNow;
            await events.SaveSnapshotAsync(item, ct);
        }, ct);
    }
}
