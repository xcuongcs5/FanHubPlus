using FanHub.AnalyticsService.DTOs;
using FanHub.AnalyticsService.Services;
using Microsoft.AspNetCore.Mvc;

namespace FanHub.AnalyticsService.Controllers;

[ApiController]
[Route("api/v1/admin")]
public class AdminDashboardController(IAnalyticsQueryService queryService) : ControllerBase
{
    /// <summary>
    /// Task 65: Dashboard Overview - Thẻ KPI thống kê số liệu tổng quan toàn sàn
    /// </summary>
    [HttpGet("dashboard/overview")]
    [HttpGet("/api/v1/analytics/admin/overview")]
    [ProducesResponseType(typeof(AdminOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview([FromQuery] string period = "month")
    {
        var overview = await queryService.GetAdminOverviewAsync(period);
        return Ok(overview);
    }

    /// <summary>
    /// Task 66: Biểu đồ tăng trưởng người dùng & DAU/MAU
    /// </summary>
    [HttpGet("dashboard/stats/users")]
    [HttpGet("/api/v1/analytics/admin/stats/users")]
    [ProducesResponseType(typeof(UserGrowthStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserStats(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var stats = await queryService.GetUserGrowthStatsAsync(from, to);
        return Ok(stats);
    }

    /// <summary>
    /// Task 67: Biểu đồ phân tích doanh thu bán vé & hoa hồng sàn
    /// </summary>
    [HttpGet("dashboard/stats/revenue")]
    [HttpGet("/api/v1/analytics/admin/stats/revenue")]
    [ProducesResponseType(typeof(RevenueStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRevenueStats(
        [FromQuery] string? group_by,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var stats = await queryService.GetRevenueStatsAsync(group_by, from, to);
        return Ok(stats);
    }

    /// <summary>
    /// Task 68: Bảng xếp hạng Fandom / Danh mục thịnh hành nhất
    /// </summary>
    [HttpGet("dashboard/stats/categories")]
    [HttpGet("/api/v1/analytics/admin/stats/categories")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategoryStats(
        [FromQuery] int limit = 10,
        [FromQuery] string sort = "popularity")
    {
        var stats = await queryService.GetCategoryPopularityAsync(limit, sort);
        return Ok(new { data = stats });
    }

    /// <summary>
    /// Task 103: Màn hình Báo cáo doanh thu bán vé & Đối soát hoa hồng sàn
    /// </summary>
    [HttpGet("financial/reports")]
    [HttpGet("/api/v1/analytics/admin/financial/reports")]
    [ProducesResponseType(typeof(FinancialReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFinancialReports(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? group_by = "event")
    {
        var report = await queryService.GetFinancialReportAsync(from, to, group_by);
        return Ok(report);
    }
}
