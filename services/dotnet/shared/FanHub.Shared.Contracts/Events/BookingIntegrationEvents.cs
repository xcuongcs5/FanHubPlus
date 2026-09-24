namespace FanHub.Shared.Contracts.Events;

// Booking's private asynchronous command; published through its transactional outbox.
public record ReservationRequestedEvent(Guid RequestId);

// Trusted catalog command from organizer/CMS, not a public booking request.
public record TicketTypeConfiguredEvent(Guid TicketTypeId, Guid EventId, string Name,
    decimal Price, string Currency, int TierRank, int TotalQuantity,
    DateTime SaleStart, DateTime SaleEnd, bool IsActive, long SourceVersion);

public record BookingCreatedEvent(Guid BookingId, Guid RequestId, Guid EventId, Guid UserId,
    decimal Amount, string Currency, DateTime ExpiresAt, string Status, long SourceVersion);
public record BookingChangedEvent(Guid BookingId, Guid EventId, Guid UserId, Guid PurchaserId,
    decimal Amount, string Currency, DateTime ExpiresAt, string Status, long SourceVersion);
public record UpgradeRequestedEvent(Guid UpgradeId, Guid BookingId, Guid EventId, Guid UserId,
    decimal Amount, string Currency, DateTime ExpiresAt);

// A confirmed settlement fact from Payment. Never accepted from HTTP clients.
public record BookingPaymentResultEvent(Guid TransactionId, Guid BookingId, Guid? UpgradeId,
    decimal Amount, string Currency, bool Succeeded);
public record BookingRefundRequestedEvent(Guid TransactionId, Guid BookingId, decimal Amount,
    string Currency, string Reason);
public record BookingRefundResultEvent(Guid TransactionId, Guid BookingId, bool Succeeded);

// Blockchain resolves the user-to-wallet mapping internally; these commands contain no private keys.
public record TicketMintRequestedEvent(Guid BookingId, Guid EventId, Guid UserId);
public record TicketMintResultEvent(Guid BookingId, bool Succeeded, string? NftTokenId,
    string? TransactionHash, string? Error);
public record TicketTransferRequestedEvent(Guid TransferId, Guid BookingId, string NftTokenId,
    Guid FromUserId, Guid ToUserId);
public record TicketTransferResultEvent(Guid TransferId, Guid BookingId, bool Succeeded,
    string? TransactionHash, string? Error);
