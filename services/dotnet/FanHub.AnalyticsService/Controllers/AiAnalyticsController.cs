using FanHub.AnalyticsService.DTOs;
using FanHub.AnalyticsService.Services;
using Microsoft.AspNetCore.Mvc;

namespace FanHub.AnalyticsService.Controllers;

[ApiController]
[Route("api/v1/analytics/ai")]
public class AiAnalyticsController(
    IAnalyticsQueryService queryService,
    ITelemetryService telemetryService) : ControllerBase
{
    /// <summary>
    /// Thống kê xu hướng câu hỏi, tỷ lệ câu hỏi chưa giải đáp và độ toxic của người dùng
    /// </summary>
    [HttpGet("query-trends")]
    [ProducesResponseType(typeof(List<AiChatTopicTrendDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQueryTrends()
    {
        var trends = await queryService.GetAiChatTrendsAsync();
        return Ok(trends);
    }

    /// <summary>
    /// Ghi nhận lịch sử hội thoại từ AI Chatbot Service để phân tích và bổ sung FAQ
    /// </summary>
    [HttpPost("record-interaction")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> RecordInteraction([FromBody] AiRecordInteractionDto dto)
    {
        await telemetryService.RecordInteractionAsync(dto);
        return Ok(new { success = true, message = "Đã lưu lịch sử hội thoại vào Analytics" });
    }
}
