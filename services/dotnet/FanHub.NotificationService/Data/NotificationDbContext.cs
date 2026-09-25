using Microsoft.EntityFrameworkCore;
namespace FanHub.NotificationService.Data;
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DeviceToken> Tokens => Set<DeviceToken>();
    public DbSet<NotificationDelivery> Deliveries => Set<NotificationDelivery>();
    public DbSet<UserProjection> Users => Set<UserProjection>();
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();
    public DbSet<BookingNotificationState> BookingStates => Set<BookingNotificationState>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Notification>().ToTable("NOTIFICATION").HasKey(x => x.NotificationId);
        model.Entity<Notification>().Property(x => x.NotificationId).HasColumnName("notification_id").HasColumnType("uniqueidentifier").ValueGeneratedNever();
        model.Entity<Notification>().Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uniqueidentifier");
        model.Entity<Notification>().Property(x => x.SourceMessageId).HasColumnName("source_message_id").HasColumnType("uniqueidentifier");
        model.Entity<Notification>().Property(x => x.Type).HasColumnName("type").HasColumnType("varchar(100)");
        model.Entity<Notification>().Property(x => x.Title).HasColumnName("title").HasColumnType("nvarchar(250)");
        model.Entity<Notification>().Property(x => x.Message).HasColumnName("message").HasColumnType("nvarchar(4000)");
        model.Entity<Notification>().Property(x => x.DataJson).HasColumnName("data_json").HasColumnType("nvarchar(max)");
        model.Entity<Notification>().Property(x => x.ReadAt).HasColumnName("read_at").HasColumnType("datetime2(3)");
        model.Entity<Notification>().Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        model.Entity<DeviceToken>().ToTable("DEVICE_TOKEN").HasKey(x => x.DeviceTokenId);
        model.Entity<DeviceToken>().Property(x => x.DeviceTokenId).HasColumnName("device_token_id").HasColumnType("uniqueidentifier").ValueGeneratedNever();
        model.Entity<DeviceToken>().Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uniqueidentifier");
        model.Entity<DeviceToken>().Property(x => x.Provider).HasColumnName("provider").HasColumnType("varchar(10)");
        model.Entity<DeviceToken>().Property(x => x.DeviceId).HasColumnName("device_id").HasColumnType("varchar(200)");
        model.Entity<DeviceToken>().Property(x => x.Token).HasColumnName("token").HasColumnType("nvarchar(2048)");
        model.Entity<DeviceToken>().Property(x => x.IsActive).HasColumnName("is_active").HasColumnType("bit");
        model.Entity<DeviceToken>().Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        model.Entity<DeviceToken>().Property(x => x.LastSeenAt).HasColumnName("last_seen_at").HasColumnType("datetime2(3)");
        model.Entity<DeviceToken>().Property(x => x.BindingId).HasColumnName("binding_id").HasColumnType("uniqueidentifier");
        model.Entity<NotificationDelivery>().ToTable("NOTIFICATION_DELIVERY").HasKey(x => x.DeliveryId);
        model.Entity<NotificationDelivery>().Property(x => x.DeliveryId).HasColumnName("delivery_id").HasColumnType("uniqueidentifier").ValueGeneratedNever();
        model.Entity<NotificationDelivery>().Property(x => x.NotificationId).HasColumnName("notification_id").HasColumnType("uniqueidentifier");
        model.Entity<NotificationDelivery>().Property(x => x.DeviceTokenId).HasColumnName("device_token_id").HasColumnType("uniqueidentifier");
        model.Entity<NotificationDelivery>().Property(x => x.Status).HasColumnName("status").HasColumnType("varchar(20)");
        model.Entity<NotificationDelivery>().Property(x => x.AttemptCount).HasColumnName("attempt_count").HasColumnType("int");
        model.Entity<NotificationDelivery>().Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at").HasColumnType("datetime2(3)");
        model.Entity<NotificationDelivery>().Property(x => x.LockedUntil).HasColumnName("locked_until").HasColumnType("datetime2(3)");
        model.Entity<NotificationDelivery>().Property(x => x.LastError).HasColumnName("last_error").HasColumnType("nvarchar(2000)");
        model.Entity<NotificationDelivery>().Property(x => x.SentAt).HasColumnName("sent_at").HasColumnType("datetime2(3)");
        model.Entity<NotificationDelivery>().Property(x => x.LockId).HasColumnName("lock_id").HasColumnType("uniqueidentifier");
        model.Entity<NotificationDelivery>().Property(x => x.BindingId).HasColumnName("binding_id").HasColumnType("uniqueidentifier");
        model.Entity<NotificationDelivery>().Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("datetime2(3)");
        model.Entity<NotificationDelivery>().Property(x => x.ProviderMessageId).HasColumnName("provider_message_id").HasColumnType("varchar(250)");
        model.Entity<UserProjection>().ToTable("USER_PROJECTION").HasKey(x => x.UserId);
        model.Entity<UserProjection>().Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uniqueidentifier").ValueGeneratedNever();
        model.Entity<UserProjection>().Property(x => x.FullName).HasColumnName("full_name").HasColumnType("nvarchar(200)");
        model.Entity<UserProjection>().Property(x => x.AvatarUrl).HasColumnName("avatar_url").HasColumnType("nvarchar(500)");
        model.Entity<UserProjection>().Property(x => x.Status).HasColumnName("status").HasColumnType("varchar(20)");
        model.Entity<UserProjection>().Property(x => x.SourceUpdatedAt).HasColumnName("source_updated_at").HasColumnType("datetime2(3)");
        model.Entity<UserProjection>().Property(x => x.SyncedAt).HasColumnName("synced_at").HasColumnType("datetime2(3)");
        model.Entity<InboxMessage>().ToTable("INBOX_MESSAGE").HasKey(x => new { x.Consumer, x.MessageId });
        model.Entity<InboxMessage>().Property(x => x.Consumer).HasColumnName("consumer").HasColumnType("varchar(150)");
        model.Entity<InboxMessage>().Property(x => x.MessageId).HasColumnName("message_id").HasColumnType("uniqueidentifier").ValueGeneratedNever();
        model.Entity<InboxMessage>().Property(x => x.ProcessedAt).HasColumnName("processed_at").HasColumnType("datetime2(3)");
        model.Entity<BookingNotificationState>().ToTable("BOOKING_NOTIFICATION_STATE").HasKey(x => x.BookingId);
        model.Entity<BookingNotificationState>().Property(x => x.BookingId).HasColumnName("booking_id").HasColumnType("uniqueidentifier").ValueGeneratedNever();
        model.Entity<BookingNotificationState>().Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uniqueidentifier");
        model.Entity<BookingNotificationState>().Property(x => x.Status).HasColumnName("status").HasColumnType("varchar(30)");
        model.Entity<BookingNotificationState>().Property(x => x.SourceVersion).HasColumnName("source_version").HasColumnType("bigint");
    }
}
