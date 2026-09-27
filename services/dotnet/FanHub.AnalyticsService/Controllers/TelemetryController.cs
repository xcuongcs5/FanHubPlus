using FanHub.AnalyticsService.DTOs;
using FanHub.AnalyticsService.Services;
using Microsoft.AspNetCore.Mvc;

namespace FanHub.AnalyticsService.Controllers;

[ApiController]
[Route("api/v1/analytics/telemetry")]
public class TelemetryController(ITelemetryService telemetryService) : ControllerBase
{
    /// <summary>
    /// Thu thập mảng sự kiện hành vi người dùng từ Frontend (Page views, clicks, interactions)
    /// </summary>
    [HttpPost("collect")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CollectTelemetry([FromBody] TelemetryBatchRequestDto request)
    {
        if (request?.Events == null || request.Events.Count == 0)
        {
            return BadRequest(new { message = "Danh sách sự kiện rỗng." });
        }

        var processed = await telemetryService.IngestBatchAsync(request.Events);
        return Ok(new { success = true, processed_events = processed });
    }
}
