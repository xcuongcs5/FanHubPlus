using Microsoft.EntityFrameworkCore;

namespace FanHub.PaymentWalletService.Data;

// SQL migrations under database/ remain the schema authority. This context does not create or migrate databases.
public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<PaymentTransaction> Transactions => Set<PaymentTransaction>();
    public DbSet<WalletLedger> Ledgers => Set<WalletLedger>();
    public DbSet<PaymentRefund> Refunds => Set<PaymentRefund>();
    public DbSet<PaymentWebhook> Webhooks => Set<PaymentWebhook>();
    public DbSet<BookingProjection> Bookings => Set<BookingProjection>();
    public DbSet<UpgradeProjection> Upgrades => Set<UpgradeProjection>();
    public DbSet<UserProjection> Users => Set<UserProjection>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        // 1. WALLET
        model.Entity<Wallet>(entity =>
        {
            entity.ToTable("WALLET");
            entity.HasKey(x => x.WalletId).HasName("PK_WALLET");
            entity.Property(x => x.WalletId).HasColumnName("wallet_id").HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uniqueidentifier").IsRequired();
            entity.Property(x => x.Currency).HasColumnName("currency").HasColumnType("char(3)").IsRequired();
            entity.Property(x => x.Balance).HasColumnName("balance").HasColumnType("decimal(19,4)").HasPrecision(19, 4);
            entity.Property(x => x.Status).HasColumnName("status").HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.Version).HasColumnName("version").IsRowVersion();

            entity.HasIndex(x => new { x.UserId, x.Currency }).IsUnique().HasDatabaseName("UQ_WALLET_user_currency");
        });

        // 2. PAYMENT_TRANSACTION
        model.Entity<PaymentTransaction>(entity =>
        {
            entity.Property(x => x.NextQueryAt).HasColumnName("next_query_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.QueryAttempts).HasColumnName("query_attempts");
            entity.ToTable("PAYMENT_TRANSACTION");
            entity.HasKey(x => x.TransactionId).HasName("PK_PAYMENT_TRANSACTION");
            entity.Property(x => x.TransactionId).HasColumnName("transaction_id").HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uniqueidentifier").IsRequired();
            entity.Property(x => x.BookingId).HasColumnName("booking_id").HasColumnType("uniqueidentifier");
            entity.Property(x => x.WalletId).HasColumnName("wallet_id").HasColumnType("uniqueidentifier");
            entity.Property(x => x.UpgradeId).HasColumnName("upgrade_id").HasColumnType("uniqueidentifier");
            entity.Property(x => x.Purpose).HasColumnName("purpose").HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
            entity.Property(x => x.Provider).HasColumnName("provider").HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
            entity.Property(x => x.MerchantReference).HasColumnName("merchant_reference").HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
            entity.Property(x => x.ProviderTransactionId).HasColumnName("provider_transaction_id").HasColumnType("varchar(150)").HasMaxLength(150);
            entity.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
            entity.Property(x => x.RequestHash).HasColumnName("request_hash").HasColumnType("binary(32)").IsRequired();
            entity.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(19,4)").HasPrecision(19, 4);
            entity.Property(x => x.Currency).HasColumnName("currency").HasColumnType("char(3)").IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
            entity.Property(x => x.CheckoutUrl).HasColumnName("checkout_url").HasColumnType("nvarchar(2048)").HasMaxLength(2048);
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.CompletedAt).HasColumnName("completed_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.Version).HasColumnName("version").IsRowVersion();

            entity.HasIndex(x => x.MerchantReference).IsUnique().HasDatabaseName("UQ_PAYMENT_TRANSACTION_reference");
            entity.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique().HasDatabaseName("UQ_PAYMENT_TRANSACTION_idempotency");
            entity.HasIndex(x => new { x.Provider, x.ProviderTransactionId }).IsUnique()
                .HasFilter("[provider_transaction_id] IS NOT NULL")
                .HasDatabaseName("UX_PAYMENT_provider_transaction");
            entity.HasIndex(x => x.BookingId).IsUnique()
                .HasFilter("[purpose] = 'Booking' AND [status] = 'Succeeded'")
                .HasDatabaseName("UX_PAYMENT_booking_success");
            entity.HasIndex(x => x.UpgradeId).IsUnique()
                .HasFilter("[purpose] = 'Upgrade' AND [status] = 'Succeeded'")
                .HasDatabaseName("UX_PAYMENT_upgrade_success");
            entity.HasIndex(x => new { x.BookingId, x.CreatedAt }).HasDatabaseName("IX_PAYMENT_booking");
            entity.HasIndex(x => new { x.UserId, x.CreatedAt, x.TransactionId }).HasDatabaseName("IX_PAYMENT_user");

            entity.HasOne(x => x.Booking)
                .WithMany(x => x.Transactions)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Wallet)
                .WithMany(x => x.Transactions)
                .HasForeignKey(x => x.WalletId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Upgrade)
                .WithMany(x => x.Transactions)
                .HasForeignKey(x => new { x.UpgradeId, x.BookingId })
                .HasPrincipalKey(x => new { x.UpgradeId, x.BookingId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 3. WALLET_LEDGER
        model.Entity<WalletLedger>(entity =>
        {
            entity.ToTable("WALLET_LEDGER");
            entity.HasKey(x => x.LedgerId).HasName("PK_WALLET_LEDGER");
            entity.Property(x => x.LedgerId).HasColumnName("ledger_id").HasColumnType("bigint").ValueGeneratedOnAdd();
            entity.Property(x => x.WalletId).HasColumnName("wallet_id").HasColumnType("uniqueidentifier").IsRequired();
            entity.Property(x => x.TransactionId).HasColumnName("transaction_id").HasColumnType("uniqueidentifier").IsRequired();
            entity.Property(x => x.RefundId).HasColumnName("refund_id").HasColumnType("uniqueidentifier");
            entity.Property(x => x.EntryType).HasColumnName("entry_type").HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
            entity.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(19,4)").HasPrecision(19, 4);
            entity.Property(x => x.BalanceAfter).HasColumnName("balance_after").HasColumnType("decimal(19,4)").HasPrecision(19, 4);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");

            entity.HasIndex(x => new { x.TransactionId, x.EntryType }).IsUnique()
                .HasFilter("[refund_id] IS NULL")
                .HasDatabaseName("UX_WALLET_LEDGER_payment");
            entity.HasIndex(x => x.RefundId).IsUnique()
                .HasFilter("[refund_id] IS NOT NULL")
                .HasDatabaseName("UX_WALLET_LEDGER_refund");
            entity.HasIndex(x => new { x.WalletId, x.CreatedAt, x.LedgerId }).HasDatabaseName("IX_WALLET_LEDGER_history");

            entity.HasOne(x => x.Wallet)
                .WithMany(x => x.Ledgers)
                .HasForeignKey(x => x.WalletId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Transaction)
                .WithMany(x => x.Ledgers)
                .HasForeignKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Refund)
                .WithMany(x => x.Ledgers)
                .HasForeignKey(x => x.RefundId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 4. PAYMENT_REFUND
        model.Entity<PaymentRefund>(entity =>
        {
            entity.Property(x => x.AttemptedAt).HasColumnName("attempted_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.NextQueryAt).HasColumnName("next_query_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(100).IsUnicode(false);
            entity.ToTable("PAYMENT_REFUND");
            entity.HasKey(x => x.RefundId).HasName("PK_PAYMENT_REFUND");
            entity.Property(x => x.RefundId).HasColumnName("refund_id").HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.TransactionId).HasColumnName("transaction_id").HasColumnType("uniqueidentifier").IsRequired();
            entity.Property(x => x.RequestedBy).HasColumnName("requested_by").HasColumnType("uniqueidentifier").IsRequired();
            entity.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
            entity.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(19,4)").HasPrecision(19, 4);
            entity.Property(x => x.Reason).HasColumnName("reason").HasColumnType("nvarchar(1000)").HasMaxLength(1000).IsRequired();
            entity.Property(x => x.ProviderRefundId).HasColumnName("provider_refund_id").HasColumnType("varchar(150)").HasMaxLength(150);
            entity.Property(x => x.Status).HasColumnName("status").HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.CompletedAt).HasColumnName("completed_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.Version).HasColumnName("version").IsRowVersion();

            entity.HasIndex(x => new { x.TransactionId, x.IdempotencyKey }).IsUnique().HasDatabaseName("UQ_PAYMENT_REFUND_request");

            entity.HasOne(x => x.Transaction)
                .WithMany(x => x.Refunds)
                .HasForeignKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 5. PAYMENT_WEBHOOK
        model.Entity<PaymentWebhook>(entity =>
        {
            entity.ToTable("PAYMENT_WEBHOOK");
            entity.HasKey(x => x.WebhookId).HasName("PK_PAYMENT_WEBHOOK");
            entity.Property(x => x.WebhookId).HasColumnName("webhook_id").HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.Provider).HasColumnName("provider").HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
            entity.Property(x => x.ProviderEventKey).HasColumnName("provider_event_key").HasColumnType("varchar(200)").HasMaxLength(200).IsRequired();
            entity.Property(x => x.TransactionId).HasColumnName("transaction_id").HasColumnType("uniqueidentifier");
            entity.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasColumnType("binary(32)").IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
            entity.Property(x => x.FailureReason).HasColumnName("failure_reason").HasColumnType("nvarchar(1000)").HasMaxLength(1000);
            entity.Property(x => x.ReceivedAt).HasColumnName("received_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.ProcessedAt).HasColumnName("processed_at").HasColumnType("datetime2(3)");

            entity.HasIndex(x => new { x.Provider, x.ProviderEventKey }).IsUnique().HasDatabaseName("UQ_PAYMENT_WEBHOOK_event");

            entity.HasOne(x => x.Transaction)
                .WithMany(x => x.Webhooks)
                .HasForeignKey(x => x.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 6. BOOKING_PROJECTION
        model.Entity<BookingProjection>(entity =>
        {
            entity.ToTable("BOOKING_PROJECTION");
            entity.HasKey(x => x.BookingId).HasName("PK_BOOKING_PROJECTION");
            entity.Property(x => x.BookingId).HasColumnName("booking_id").HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uniqueidentifier").IsRequired();
            entity.Property(x => x.EventId).HasColumnName("event_id").HasColumnType("uniqueidentifier").IsRequired();
            entity.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(19,4)").HasPrecision(19, 4);
            entity.Property(x => x.Currency).HasColumnName("currency").HasColumnType("char(3)").IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasColumnType("varchar(30)").HasMaxLength(30).IsRequired();
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.SourceVersion).HasColumnName("source_version").HasColumnType("bigint");
            entity.Property(x => x.SyncedAt).HasColumnName("synced_at").HasColumnType("datetime2(3)");
        });

        // 7. UPGRADE_PROJECTION
        model.Entity<UpgradeProjection>(entity =>
        {
            entity.ToTable("UPGRADE_PROJECTION");
            entity.HasKey(x => x.UpgradeId).HasName("PK_UPGRADE_PROJECTION");
            entity.Property(x => x.UpgradeId).HasColumnName("upgrade_id").HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.BookingId).HasColumnName("booking_id").HasColumnType("uniqueidentifier").IsRequired();
            entity.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uniqueidentifier").IsRequired();
            entity.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(19,4)").HasPrecision(19, 4);
            entity.Property(x => x.Currency).HasColumnName("currency").HasColumnType("char(3)").IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.SourceVersion).HasColumnName("source_version").HasColumnType("bigint");
            entity.Property(x => x.SyncedAt).HasColumnName("synced_at").HasColumnType("datetime2(3)");

            entity.HasIndex(x => new { x.UpgradeId, x.BookingId }).IsUnique().HasDatabaseName("UQ_UPGRADE_PROJECTION_booking");

            entity.HasOne(x => x.Booking)
                .WithMany(x => x.Upgrades)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // 8. USER_PROJECTION
        model.Entity<UserProjection>(entity =>
        {
            entity.ToTable("USER_PROJECTION");
            entity.HasKey(x => x.UserId).HasName("PK_USER_PROJECTION");
            entity.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.FullName).HasColumnName("full_name").HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
            entity.Property(x => x.AvatarUrl).HasColumnName("avatar_url").HasColumnType("nvarchar(500)").HasMaxLength(500);
            entity.Property(x => x.Status).HasColumnName("status").HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
            entity.Property(x => x.SourceUpdatedAt).HasColumnName("source_updated_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.SyncedAt).HasColumnName("synced_at").HasColumnType("datetime2(3)");
        });

        // 9. OUTBOX_MESSAGE
        model.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OUTBOX_MESSAGE");
            entity.HasKey(x => x.MessageId).HasName("PK_OUTBOX_MESSAGE");
            entity.Property(x => x.MessageId).HasColumnName("message_id").HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.EventType).HasColumnName("event_type").HasColumnType("varchar(200)").HasMaxLength(200).IsRequired();
            entity.Property(x => x.SchemaVersion).HasColumnName("schema_version").HasColumnType("int");
            entity.Property(x => x.AggregateId).HasColumnName("aggregate_id").HasColumnType("uniqueidentifier").IsRequired();
            entity.Property(x => x.AggregateVersion).HasColumnName("aggregate_version").HasColumnType("bigint");
            entity.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasColumnType("uniqueidentifier");
            entity.Property(x => x.Payload).HasColumnName("payload").HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.PublishedAt).HasColumnName("published_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasColumnType("int");
            entity.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at").HasColumnType("datetime2(3)");
            entity.Property(x => x.LockedUntil).HasColumnName("locked_until").HasColumnType("datetime2(3)");
            entity.Property(x => x.LockId).HasColumnName("lock_id").HasColumnType("uniqueidentifier");
            entity.Property(x => x.LastError).HasColumnName("last_error").HasColumnType("nvarchar(2000)").HasMaxLength(2000);

            entity.HasIndex(x => new { x.AggregateId, x.AggregateVersion, x.EventType })
                .IsUnique()
                .HasDatabaseName("UQ_OUTBOX_MESSAGE_version");
            entity.HasIndex(x => new { x.NextAttemptAt, x.OccurredAt })
                .HasFilter("[published_at] IS NULL")
                .HasDatabaseName("IX_OUTBOX_MESSAGE_pending");
        });

        // 10. INBOX_MESSAGE
        model.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("INBOX_MESSAGE");
            entity.HasKey(x => new { x.Consumer, x.MessageId }).HasName("PK_INBOX_MESSAGE");
            entity.Property(x => x.Consumer).HasColumnName("consumer").HasColumnType("varchar(150)").HasMaxLength(150);
            entity.Property(x => x.MessageId).HasColumnName("message_id").HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.ProcessedAt).HasColumnName("processed_at").HasColumnType("datetime2(3)");
        });
    }
}
