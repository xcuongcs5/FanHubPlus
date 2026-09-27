using System.Text.Json;
using FanHub.AnalyticsService.Data;
using FanHub.AnalyticsService.Data.Entities;
using FanHub.Shared.Contracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace FanHub.AnalyticsService.Consumers;

public class AnalyticsEventConsumers(
    AnalyticsDbContext db,
    ILogger<AnalyticsEventConsumers> logger) :
    IConsumer<UserCreatedEvent>,
    IConsumer<UserBannedEvent>,
    IConsumer<BookingCreatedEvent>,
    IConsumer<BookingPaymentResultEvent>,
    IConsumer<BookingRefundResultEvent>,
    IConsumer<BookingAttendeeChangedEvent>,
    IConsumer<EventChangedEvent>
{
    public async Task Consume(ConsumeContext<UserCreatedEvent> context)
    {
        var msg = context.Message;
        logger.LogInformation("Analytics received UserCreatedEvent for UserId: {UserId}", msg.UserId);

        var existing = await db.FactUserRegistrations.FirstOrDefaultAsync(u => u.UserId == msg.UserId);
        if (existing == null)
        {
            db.FactUserRegistrations.Add(new FactUserRegistration
            {
                UserId = msg.UserId,
                Email = msg.Email,
                FullName = msg.FullName,
                Role = "User",
                CreatedAt = msg.CreatedAt
            });
            await db.SaveChangesAsync();
        }
    }

    public async Task Consume(ConsumeContext<UserBannedEvent> context)
    {
        var msg = context.Message;
        logger.LogInformation("Analytics received UserBannedEvent for UserId: {UserId}", msg.UserId);

        var user = await db.FactUserRegistrations.FirstOrDefaultAsync(u => u.UserId == msg.UserId);
        if (user != null)
        {
            user.IsBanned = true;
            user.BannedAt = msg.BannedAt;
            await db.SaveChangesAsync();
        }
    }

    public async Task Consume(ConsumeContext<BookingCreatedEvent> context)
    {
        var msg = context.Message;
        logger.LogInformation("Analytics received BookingCreatedEvent for BookingId: {BookingId}", msg.BookingId);

        var existing = await db.FactTicketSales.FirstOrDefaultAsync(s => s.BookingId == msg.BookingId);
        if (existing == null)
        {
            db.FactTicketSales.Add(new FactTicketSale
            {
                BookingId = msg.BookingId,
                EventId = msg.EventId,
                UserId = msg.UserId,
                Amount = msg.Amount,
                Currency = msg.Currency,
                Status = msg.Status,
                OccurredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }

    public async Task Consume(ConsumeContext<BookingPaymentResultEvent> context)
    {
        var msg = context.Message;
        logger.LogInformation("Analytics received BookingPaymentResultEvent for BookingId: {BookingId}, Succeeded: {Succeeded}", msg.BookingId, msg.Succeeded);

        var sale = await db.FactTicketSales.FirstOrDefaultAsync(s => s.BookingId == msg.BookingId);
        if (sale != null)
        {
            if (msg.Succeeded)
            {
                sale.Status = "Paid";
                sale.PaidAt = DateTime.UtcNow;
                // Commission 5% based on system settings
                sale.CommissionAmount = Math.Round(sale.Amount * 0.05m, 2);
            }
            else
            {
                sale.Status = "PaymentFailed";
            }
            await db.SaveChangesAsync();
        }
    }

    public async Task Consume(ConsumeContext<BookingRefundResultEvent> context)
    {
        var msg = context.Message;
        logger.LogInformation("Analytics received BookingRefundResultEvent for BookingId: {BookingId}, Succeeded: {Succeeded}", msg.BookingId, msg.Succeeded);

        if (msg.Succeeded)
        {
            var sale = await db.FactTicketSales.FirstOrDefaultAsync(s => s.BookingId == msg.BookingId);
            if (sale != null)
            {
                sale.Status = "Refunded";
                sale.RefundedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
    }

    public async Task Consume(ConsumeContext<BookingAttendeeChangedEvent> context)
    {
        var msg = context.Message;
        logger.LogInformation("Analytics received BookingAttendeeChangedEvent for BookingId: {BookingId}, Status: {Status}", msg.BookingId, msg.Status);

        if (msg.CheckedInAt.HasValue)
        {
            var existing = await db.FactEventAttendances.FirstOrDefaultAsync(a => a.BookingId == msg.BookingId);
            if (existing == null)
            {
                db.FactEventAttendances.Add(new FactEventAttendance
                {
                    BookingId = msg.BookingId,
                    EventId = msg.EventId,
                    UserId = msg.UserId,
                    Status = msg.Status,
                    CheckedInAt = msg.CheckedInAt.Value
                });
                await db.SaveChangesAsync();
            }
        }
    }

    public async Task Consume(ConsumeContext<EventChangedEvent> context)
    {
        var msg = context.Message;
        logger.LogInformation("Analytics received EventChangedEvent for EventId: {EventId}", msg.EventId);

        var evt = await db.FactEventMetadata.FirstOrDefaultAsync(e => e.EventId == msg.EventId);
        if (evt == null)
        {
            db.FactEventMetadata.Add(new FactEventMetadata
            {
                EventId = msg.EventId,
                OrganizerId = msg.OrganizerId,
                Title = msg.Title,
                StartTime = msg.StartTime,
                EndTime = msg.EndTime,
                Capacity = msg.Capacity,
                Status = msg.Status,
                IsFeatured = msg.IsFeatured,
                LocationName = msg.LocationName,
                CategoryIdsJson = JsonSerializer.Serialize(msg.CategoryIds ?? Array.Empty<Guid>()),
                LastUpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            evt.Title = msg.Title;
            evt.StartTime = msg.StartTime;
            evt.EndTime = msg.EndTime;
            evt.Capacity = msg.Capacity;
            evt.Status = msg.Status;
            evt.IsFeatured = msg.IsFeatured;
            evt.LocationName = msg.LocationName;
            evt.CategoryIdsJson = JsonSerializer.Serialize(msg.CategoryIds ?? Array.Empty<Guid>());
            evt.LastUpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }
}
