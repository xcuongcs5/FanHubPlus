namespace FanHub.BookingService.Data;

public sealed class EventProjection
{
    public Guid EventId { get; set; }
    public Guid OrganizerId { get; set; }
    public string Title { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int Capacity { get; set; }
    public string Status { get; set; } = "";
    public long SourceVersion { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}

public sealed class StaffProjection
{
    public Guid EventId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "";
    public bool IsActive { get; set; }
    public long SourceVersion { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TicketType
{
    public Guid TicketTypeId { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Currency { get; set; } = "";
    public int TierRank { get; set; }
    public int TotalQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int SoldQuantity { get; set; }
    public DateTime SaleStart { get; set; }
    public DateTime SaleEnd { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] Version { get; set; } = [];
    public long SourceVersion { get; set; }
}

public sealed class BookingRequest
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public byte[] RequestHash { get; set; } = [];
    public Guid EventId { get; set; }
    public Guid TicketTypeId { get; set; }
    public int Quantity { get; set; }
    public string Status { get; set; } = "";
    public string? FailureCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class TicketBooking
{
    public Guid BookingId { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public int TicketNumber { get; set; }
    public Guid EventId { get; set; }
    public Guid TicketTypeId { get; set; }
    public Guid PurchaserId { get; set; }
    public Guid UserId { get; set; }
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public string? NftTokenId { get; set; }
    public string? BlockchainTxHash { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public byte[] Version { get; set; } = [];
    public string? BlockchainError { get; set; }
}

public sealed class TicketTransfer
{
    public Guid TransferId { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string Status { get; set; } = "";
    public string? BlockchainTxHash { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public sealed class TicketUpgrade
{
    public Guid UpgradeId { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public Guid EventId { get; set; }
    public Guid FromTicketTypeId { get; set; }
    public Guid ToTicketTypeId { get; set; }
    public decimal PriceDifference { get; set; }
    public string Currency { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public sealed class TicketCheckIn
{
    public Guid BookingId { get; set; }
    public Guid StaffUserId { get; set; }
    public byte[] QrPayloadHash { get; set; } = [];
    public DateTime CheckedInAt { get; set; } = DateTime.UtcNow;
}

public sealed class UserProjection
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string Status { get; set; } = "";
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

public sealed class BookingPayment
{
    public Guid TransactionId { get; set; }
    public Guid BookingId { get; set; }
    public Guid? UpgradeId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Status { get; set; } = "";
    public string? RefundReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
