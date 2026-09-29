using FanHub.AnalyticsService.Controllers;
using FanHub.AnalyticsService.Data;
using FanHub.AnalyticsService.Data.Entities;
using FanHub.AnalyticsService.DTOs;
using FanHub.AnalyticsService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FanHub.AnalyticsService.Tests;

public class AnalyticsServiceTests
{
    private AnalyticsDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AnalyticsDbContext(options);
    }

    [Fact]
    public async Task GetAdminOverview_ReturnsAccurateMetrics()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var redis = new RedisMetricsService(NullLogger<RedisMetricsService>.Instance, null);
        var queryService = new AnalyticsQueryService(db, redis, NullLogger<AnalyticsQueryService>.Instance);
        var controller = new AdminDashboardController(queryService);

        // Seed
        db.FactUserRegistrations.Add(new FactUserRegistration
        {
            UserId = Guid.NewGuid(),
            Email = "test@fanhub.com",
            FullName = "Test User",
            CreatedAt = DateTime.UtcNow
        });

        var eventId = Guid.NewGuid();
        db.FactEventMetadata.Add(new FactEventMetadata
        {
            EventId = eventId,
            OrganizerId = Guid.NewGuid(),
            Title = "Test Concert",
            Status = "Published",
            Capacity = 1000
        });

        db.FactTicketSales.Add(new FactTicketSale
        {
            BookingId = Guid.NewGuid(),
            EventId = eventId,
            UserId = Guid.NewGuid(),
            Amount = 500000m,
            Status = "Paid",
            OccurredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Act
        var actionResult = await controller.GetOverview("month");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var overview = Assert.IsType<AdminOverviewDto>(okResult.Value);

        Assert.Equal(1, overview.TotalUsers);
        Assert.Equal(1, overview.TotalEvents);
        Assert.Equal(500000m, overview.TotalRevenue);
    }

    [Fact]
    public async Task TelemetryBatchIngest_ProcessesAllEventsSuccessfully()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var redis = new RedisMetricsService(NullLogger<RedisMetricsService>.Instance, null);
        var telemetryService = new TelemetryService(db, redis, NullLogger<TelemetryService>.Instance);
        var controller = new TelemetryController(telemetryService);

        var batch = new TelemetryBatchRequestDto
        {
            Events = new List<TelemetryItemDto>
            {
                new() { EventType = "page_view", TargetId = "/events/123" },
                new() { EventType = "category_view", TargetId = "Esports" }
            }
        };

        // Act
        var result = await controller.CollectTelemetry(batch);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(2, await db.FactTelemetryEvents.CountAsync());
    }

    [Fact]
    public async Task OrganizerEventSummary_CalculatesAccurateAttendanceRates()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var redis = new RedisMetricsService(NullLogger<RedisMetricsService>.Instance, null);
        var queryService = new AnalyticsQueryService(db, redis, NullLogger<AnalyticsQueryService>.Instance);
        var controller = new OrganizerAnalyticsController(queryService);

        var eventId = Guid.NewGuid();
        db.FactEventMetadata.Add(new FactEventMetadata
        {
            EventId = eventId,
            OrganizerId = Guid.NewGuid(),
            Title = "Cosplay Festival",
            Capacity = 200,
            Status = "Published"
        });

        var booking1 = Guid.NewGuid();
        var booking2 = Guid.NewGuid();

        db.FactTicketSales.AddRange(
            new FactTicketSale { BookingId = booking1, EventId = eventId, Amount = 100000, Status = "Paid" },
            new FactTicketSale { BookingId = booking2, EventId = eventId, Amount = 100000, Status = "Paid" }
        );

        db.FactEventAttendances.Add(new FactEventAttendance
        {
            BookingId = booking1,
            EventId = eventId,
            UserId = Guid.NewGuid(),
            CheckedInAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        // Act
        var actionResult = await controller.GetEventSummary(eventId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var summary = Assert.IsType<OrganizerEventSummaryDto>(okResult.Value);

        Assert.Equal(2, summary.TotalTicketsSold);
        Assert.Equal(200000m, summary.TotalRevenue);
        Assert.Equal(1, summary.TotalCheckedIn);
        Assert.Equal("50.0%", summary.AttendanceRate); // 1 check-in out of 2 paid tickets
    }
}
