using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace FanHub.EventService.Data;

// Mapping only: SQL migrations under database/ remain the schema authority.
public sealed class EventDbContext(DbContextOptions<EventDbContext> options) : DbContext(options)
{
    public DbSet<EventEntity> Events => Set<EventEntity>();
    public DbSet<LocationEntity> Locations => Set<LocationEntity>();
    public DbSet<CategoryProjection> Categories => Set<CategoryProjection>();
    public DbSet<EventCategory> EventCategories => Set<EventCategory>();
    public DbSet<EventStaff> Staffs => Set<EventStaff>();
    public DbSet<EventReview> Reviews => Set<EventReview>();
    public DbSet<AttendeeProjection> Attendees => Set<AttendeeProjection>();
    public DbSet<UserProjection> Users => Set<UserProjection>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<EventEntity>().ToTable("EVENT").HasKey(x => x.EventId);
        model.Entity<LocationEntity>().ToTable("LOCATION").HasKey(x => x.LocationId);
        model.Entity<CategoryProjection>().ToTable("CATEGORY_PROJECTION").HasKey(x => x.CategoryId);
        model.Entity<EventCategory>().ToTable("EVENT_CATEGORY").HasKey(x => new { x.EventId, x.CategoryId });
        model.Entity<EventStaff>().ToTable("EVENT_STAFF").HasKey(x => new { x.EventId, x.UserId });
        model.Entity<EventReview>().ToTable("EVENT_REVIEW").HasKey(x => x.ReviewId);
        model.Entity<AttendeeProjection>().ToTable("ATTENDEE_PROJECTION").HasKey(x => x.BookingId);
        model.Entity<UserProjection>().ToTable("USER_PROJECTION").HasKey(x => x.UserId);
        model.Entity<OutboxMessage>().ToTable("OUTBOX_MESSAGE").HasKey(x => x.MessageId);
        model.Entity<InboxMessage>().ToTable("INBOX_MESSAGE").HasKey(x => new { x.Consumer, x.MessageId });

        foreach (var entity in model.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
        {
            property.SetColumnName(JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name));
            if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                property.SetColumnType("datetime2(3)");
            // Projection IDs originate outside this database; EF must not generate them.
            if (property.ClrType == typeof(Guid)) property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }
        model.Entity<EventEntity>().Property(x => x.Version).IsRowVersion();
        model.Entity<LocationEntity>().Property(x => x.Latitude).HasPrecision(9, 6);
        model.Entity<LocationEntity>().Property(x => x.Longitude).HasPrecision(9, 6);
        model.Entity<EventEntity>().HasOne<LocationEntity>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<EventCategory>().HasOne<EventEntity>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<EventCategory>().HasOne<CategoryProjection>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<EventStaff>().HasOne<EventEntity>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<EventReview>().HasOne<EventEntity>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}
