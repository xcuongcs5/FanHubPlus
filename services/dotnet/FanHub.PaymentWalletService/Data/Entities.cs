namespace FanHub.PaymentWalletService.Data;

public static class PaymentConstants
{
    public static class WalletStatus
    {
        public const string Active = "Active";
        public const string Frozen = "Frozen";
        public const string Closed = "Closed";
    }

    public static class Purpose
    {
        public const string Booking = "Booking";
        public const string Upgrade = "Upgrade";
        public const string Deposit = "Deposit";
    }

    public static class Provider
    {
        public const string VNPay = "VNPay";
        public const string MoMo = "MoMo";
        public const string Wallet = "Wallet";
    }

    public static class Status
    {
        public const string Pending = "Pending";
        public const string Processing = "Processing";
        public const string Succeeded = "Succeeded";
        public const string Failed = "Failed";
        public const string Expired = "Expired";
        public const string Cancelled = "Cancelled";
    }

    public static class RefundStatus
    {
        public const string Pending = "Pending";
        public const string Processing = "Processing";
        public const string Succeeded = "Succeeded";
        public const string Failed = "Failed";
    }

    public static class WebhookStatus
    {
        public const string Received = "Received";
        public const string Processed = "Processed";
        public const string Rejected = "Rejected";
        public const string Failed = "Failed";
    }

    public static class LedgerEntryType
    {
        public const string Deposit = "Deposit";
        public const string Payment = "Payment";
        public const string Refund = "Refund";
    }
}

public sealed class Wallet
{
    public Guid WalletId { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Currency { get; set; } = "VND";
    public decimal Balance { get; set; } = 0m;
    public string Status { get; set; } = PaymentConstants.WalletStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public byte[] Version { get; set; } = [];

    // Navigation
    public ICollection<PaymentTransaction> Transactions { get; set; } = new List<PaymentTransaction>();
    public ICollection<WalletLedger> Ledgers { get; set; } = new List<WalletLedger>();
}

public sealed class PaymentTransaction
{
    public DateTime? NextQueryAt { get; set; }
    public int QueryAttempts { get; set; }
    public Guid TransactionId { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? WalletId { get; set; }
    public Guid? UpgradeId { get; set; }
    public string Purpose { get; set; } = "";
    public string Provider { get; set; } = "";
    public string MerchantReference { get; set; } = "";
    public string? ProviderTransactionId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public byte[] RequestHash { get; set; } = [];
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string Status { get; set; } = PaymentConstants.Status.Pending;
    public string? CheckoutUrl { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public byte[] Version { get; set; } = [];

    // Navigation
    public BookingProjection? Booking { get; set; }
    public Wallet? Wallet { get; set; }
    public UpgradeProjection? Upgrade { get; set; }
    public ICollection<WalletLedger> Ledgers { get; set; } = new List<WalletLedger>();
    public ICollection<PaymentRefund> Refunds { get; set; } = new List<PaymentRefund>();
    public ICollection<PaymentWebhook> Webhooks { get; set; } = new List<PaymentWebhook>();
}

public sealed class WalletLedger
{
    public long LedgerId { get; set; }
    public Guid WalletId { get; set; }
    public Guid TransactionId { get; set; }
    public Guid? RefundId { get; set; }
    public string EntryType { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Wallet Wallet { get; set; } = null!;
    public PaymentTransaction Transaction { get; set; } = null!;
    public PaymentRefund? Refund { get; set; }
}

public sealed class PaymentRefund
{
    public DateTime? AttemptedAt { get; set; }
    public DateTime? NextQueryAt { get; set; }
    public string? LastError { get; set; }
    public Guid RefundId { get; set; } = Guid.NewGuid();
    public Guid TransactionId { get; set; }
    public Guid RequestedBy { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public decimal Amount { get; set; }
    public string Reason { get; set; } = "";
    public string? ProviderRefundId { get; set; }
    public string Status { get; set; } = PaymentConstants.RefundStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public byte[] Version { get; set; } = [];

    // Navigation
    public PaymentTransaction Transaction { get; set; } = null!;
    public ICollection<WalletLedger> Ledgers { get; set; } = new List<WalletLedger>();
}

public sealed class PaymentWebhook
{
    public Guid WebhookId { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = "";
    public string ProviderEventKey { get; set; } = "";
    public Guid? TransactionId { get; set; }
    public byte[] PayloadHash { get; set; } = [];
    public string Status { get; set; } = PaymentConstants.WebhookStatus.Received;
    public string? FailureReason { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }

    // Navigation
    public PaymentTransaction? Transaction { get; set; }
}

public sealed class BookingProjection
{
    public Guid BookingId { get; set; }
    public Guid UserId { get; set; }
    public Guid EventId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string Status { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public long SourceVersion { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<PaymentTransaction> Transactions { get; set; } = new List<PaymentTransaction>();
    public ICollection<UpgradeProjection> Upgrades { get; set; } = new List<UpgradeProjection>();
}

public sealed class UpgradeProjection
{
    public Guid UpgradeId { get; set; }
    public Guid BookingId { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string Status { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public long SourceVersion { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public BookingProjection Booking { get; set; } = null!;
    public ICollection<PaymentTransaction> Transactions { get; set; } = new List<PaymentTransaction>();
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
