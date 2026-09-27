using FanHub.AnalyticsService.Data;
using FanHub.AnalyticsService.Data.Entities;
using FanHub.AnalyticsService.DTOs;

namespace FanHub.AnalyticsService.Services;

public class TelemetryService(
    AnalyticsDbContext db,
    IRedisMetricsService redis,
    ILogger<TelemetryService> logger) : ITelemetryService
{
    public async Task<int> IngestBatchAsync(IEnumerable<TelemetryItemDto> events)
    {
        var count = 0;
        foreach (var item in events)
        {
            var entity = new FactTelemetryEvent
            {
                EventType = item.EventType,
                TargetId = item.TargetId,
                SessionId = item.SessionId,
                UserId = item.UserId,
                MetadataJson = item.Metadata,
                Timestamp = item.Timestamp ?? DateTime.UtcNow
            };

            db.FactTelemetryEvents.Add(entity);
            count++;

            // If user is logged in, mark user active in Redis
            if (item.UserId.HasValue && item.UserId.Value != Guid.Empty)
            {
                await redis.RecordUserActiveAsync(item.UserId.Value);
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Ingested {Count} telemetry events", count);
        return count;
    }

    public async Task RecordInteractionAsync(AiRecordInteractionDto dto)
    {
        var entity = new FactChatInteraction
        {
            SessionId = dto.SessionId,
            UserId = dto.UserId,
            UserQuery = dto.UserQuery,
            BotResponse = dto.BotResponse,
            IsResolved = dto.IsResolved,
            ToxicScore = dto.ToxicScore,
            Topic = dto.Topic,
            OccurredAt = DateTime.UtcNow
        };

        db.FactChatInteractions.Add(entity);
        await db.SaveChangesAsync();

        if (dto.UserId.HasValue && dto.UserId.Value != Guid.Empty)
        {
            await redis.RecordUserActiveAsync(dto.UserId.Value);
        }
    }
}
