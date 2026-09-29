using FanHub.AnalyticsService.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FanHub.AnalyticsService.Data;

public class AnalyticsDbContext : DbContext
{
    public AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : base(options)
    {
    }

    public DbSet<FactTicketSale> FactTicketSales => Set<FactTicketSale>();
    public DbSet<FactEventAttendance> FactEventAttendances => Set<FactEventAttendance>();
    public DbSet<FactUserRegistration> FactUserRegistrations => Set<FactUserRegistration>();
    public DbSet<FactEventMetadata> FactEventMetadata => Set<FactEventMetadata>();
    public DbSet<FactTelemetryEvent> FactTelemetryEvents => Set<FactTelemetryEvent>();
    public DbSet<FactChatInteraction> FactChatInteractions => Set<FactChatInteraction>();
    public DbSet<AggDailyPlatformKpi> AggDailyPlatformKpis => Set<AggDailyPlatformKpi>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FactTicketSale>(entity =>
        {
            entity.HasIndex(e => e.OccurredAt);
            entity.HasIndex(e => e.EventId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.BookingId).IsUnique();
        });

        modelBuilder.Entity<FactEventAttendance>(entity =>
        {
            entity.HasIndex(e => e.EventId);
            entity.HasIndex(e => e.CheckedInAt);
            entity.HasIndex(e => e.BookingId).IsUnique();
        });

        modelBuilder.Entity<FactUserRegistration>(entity =>
        {
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.UserId).IsUnique();
        });

        modelBuilder.Entity<FactEventMetadata>(entity =>
        {
            entity.HasIndex(e => e.OrganizerId);
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<FactTelemetryEvent>(entity =>
        {
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.EventType);
            entity.HasIndex(e => e.TargetId);
        });

        modelBuilder.Entity<FactChatInteraction>(entity =>
        {
            entity.HasIndex(e => e.OccurredAt);
            entity.HasIndex(e => e.Topic);
        });

        modelBuilder.Entity<AggDailyPlatformKpi>(entity =>
        {
            entity.HasIndex(e => e.Date).IsUnique();
        });
    }
}
