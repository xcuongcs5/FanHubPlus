using System.Text.Json;
using FanHub.BookingService.Data;
using FanHub.Shared.Contracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace FanHub.BookingService.Messaging;

public sealed class OutboxPublisher(IServiceScopeFactory scopes, ILogger<OutboxPublisher> logger) : BackgroundService
{
    private static readonly IReadOnlyDictionary<string, Type> MessageTypes = new Dictionary<string, Type>
    {
        [nameof(ReservationRequestedEvent)] = typeof(ReservationRequestedEvent),
        [nameof(BookingCreatedEvent)] = typeof(BookingCreatedEvent),
        [nameof(BookingChangedEvent)] = typeof(BookingChangedEvent),
        [nameof(BookingAttendeeChangedEvent)] = typeof(BookingAttendeeChangedEvent),
        [nameof(UpgradeRequestedEvent)] = typeof(UpgradeRequestedEvent),
        [nameof(BookingRefundRequestedEvent)] = typeof(BookingRefundRequestedEvent),
        [nameof(TicketMintRequestedEvent)] = typeof(TicketMintRequestedEvent),
        [nameof(TicketTransferRequestedEvent)] = typeof(TicketTransferRequestedEvent)
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await PublishOneAsync(stoppingToken)) await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox database operation failed; retrying.");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    public async Task<bool> PublishOneAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var now = DateTime.UtcNow;
        var candidate = await db.Outbox.AsNoTracking().Where(x => x.PublishedAt == null && x.AttemptCount < 20 &&
            x.NextAttemptAt <= now && (x.LockedUntil == null || x.LockedUntil < now))
            .OrderBy(x => x.OccurredAt).ThenBy(x => x.AggregateVersion).FirstOrDefaultAsync(ct);
        if (candidate is null) return false;
        var lockId = Guid.NewGuid();
        var claimed = await db.Outbox.Where(x => x.MessageId == candidate.MessageId && x.PublishedAt == null &&
            (x.LockedUntil == null || x.LockedUntil < now))
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.LockId, lockId)
                .SetProperty(x => x.LockedUntil, now.AddSeconds(60)), ct);
        if (claimed == 0) return true;
        var owned = db.Outbox.Where(x => x.MessageId == candidate.MessageId && x.LockId == lockId);
        try
        {
            if (!MessageTypes.TryGetValue(candidate.EventType, out var type)) throw new InvalidOperationException("Unknown outbox event type.");
            var message = JsonSerializer.Deserialize(candidate.Payload, type) ?? throw new InvalidOperationException("Invalid outbox payload.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await scope.ServiceProvider.GetRequiredService<IPublishEndpoint>().Publish(message, type, context =>
            {
                context.MessageId = candidate.MessageId; context.CorrelationId = candidate.CorrelationId;
                context.Headers.Set("schema_version", candidate.SchemaVersion);
                context.Headers.Set("aggregate_version", candidate.AggregateVersion);
            }, timeout.Token);
            await owned.ExecuteUpdateAsync(set => set.SetProperty(x => x.PublishedAt, DateTime.UtcNow)
                .SetProperty(x => x.LockId, (Guid?)null).SetProperty(x => x.LockedUntil, (DateTime?)null)
                .SetProperty(x => x.LastError, (string?)null), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning("Outbox message {MessageId} publish failed ({ErrorType}).", candidate.MessageId, exception.GetType().Name);
            var next = DateTime.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(candidate.AttemptCount + 1, 8))));
            await owned.ExecuteUpdateAsync(set => set.SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                .SetProperty(x => x.NextAttemptAt, next).SetProperty(x => x.LockId, (Guid?)null)
                .SetProperty(x => x.LockedUntil, (DateTime?)null).SetProperty(x => x.LastError, exception.GetType().Name), ct);
        }
        return true;
    }
}
