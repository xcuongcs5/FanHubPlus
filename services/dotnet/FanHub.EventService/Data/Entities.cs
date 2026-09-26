namespace FanHub.EventService.Data;

public sealed class EventEntity
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public Guid OrganizerId { get; set; }
    public Guid LocationId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? BannerUrl { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int Capacity { get; set; }
    public string Status { get; set; } = "Draft";
    public bool IsFeatured { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public Guid? ReviewRequestId { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class LocationEntity
{
    public Guid LocationId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class CategoryProjection
{
    public Guid CategoryId { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "";
    public bool IsDeleted { get; set; }
    public long SourceVersion { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}

public sealed class EventCategory
{
    public Guid EventId { get; set; }
    public Guid CategoryId { get; set; }
}

public sealed class EventStaff
{
    public Guid EventId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "CheckIn";
    public bool IsActive { get; set; } = true;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}

public sealed class EventReview
{
    public Guid ReviewId { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public Guid? ReviewRequestId { get; set; }
    public Guid? ReviewerId { get; set; }
    public string Source { get; set; } = "";
    public string Decision { get; set; } = "";
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AttendeeProjection
{
    public Guid BookingId { get; set; }
    public Guid EventId { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = "";
    public DateTime? CheckedInAt { get; set; }
    public long SourceVersion { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}

public sealed class UserProjection
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime SourceUpdatedAt { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}

public sealed class OutboxMessage
{
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public Guid AggregateId { get; set; }
    public long AggregateVersion { get; set; }
    public Guid? CorrelationId { get; set; }
    public string Payload { get; set; } = "";
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime? LockedUntil { get; set; }
    public Guid? LockId { get; set; }
    public string? LastError { get; set; }
}

public sealed class InboxMessage
{
    public string Consumer { get; set; } = "";
    public Guid MessageId { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
