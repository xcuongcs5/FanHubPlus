using FanHub.AnalyticsService.DTOs;
using FanHub.AnalyticsService.Services;
using Microsoft.AspNetCore.Mvc;

namespace FanHub.AnalyticsService.Controllers;

[ApiController]
[Route("api/v1/analytics/organizer")]
public class OrganizerAnalyticsController(IAnalyticsQueryService queryService) : ControllerBase
{
    /// <summary>
    /// Báo cáo thống kê hiệu suất bán vé & tỷ lệ check-in của một sự kiện
    /// </summary>
    [HttpGet("events/{eventId}/summary")]
    [ProducesResponseType(typeof(OrganizerEventSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEventSummary(Guid eventId)
    {
        var summary = await queryService.GetOrganizerEventSummaryAsync(eventId);
        return Ok(summary);
    }
}
