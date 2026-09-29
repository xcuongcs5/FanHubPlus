using FanHub.AnalyticsService.DTOs;

namespace FanHub.AnalyticsService.Services;

public interface ITelemetryService
{
    Task<int> IngestBatchAsync(IEnumerable<TelemetryItemDto> events);
    Task RecordInteractionAsync(AiRecordInteractionDto dto);
}
