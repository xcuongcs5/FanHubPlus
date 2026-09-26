namespace FanHub.NotificationService.Data;

public sealed class Notification
{
    public Guid NotificationId { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid SourceMessageId { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? DataJson { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class DeviceToken
{
    public Guid DeviceTokenId { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Provider { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Token { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public Guid BindingId { get; set; } = Guid.NewGuid();
}

public sealed class NotificationDelivery
{
    public Guid DeliveryId { get; set; } = Guid.NewGuid();
    public Guid NotificationId { get; set; }
    public Guid DeviceTokenId { get; set; }
    public string Status { get; set; } = "";
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public DateTime? SentAt { get; set; }
    public Guid? LockId { get; set; }
    public Guid BindingId { get; set; } = Guid.NewGuid();
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow;
    public string? ProviderMessageId { get; set; }
}

public sealed class UserProjection
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string Status { get; set; } = "";
    public DateTime SourceUpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}

public sealed class InboxMessage
{
    public string Consumer { get; set; } = "";
    public Guid MessageId { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

public sealed class BookingNotificationState
{
    public Guid BookingId { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = "";
    public long SourceVersion { get; set; }
}
