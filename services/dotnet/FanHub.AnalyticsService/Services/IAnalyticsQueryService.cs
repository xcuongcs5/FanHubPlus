using FanHub.AnalyticsService.DTOs;

namespace FanHub.AnalyticsService.Services;

public interface IAnalyticsQueryService
{
    Task<AdminOverviewDto> GetAdminOverviewAsync(string period);
    Task<UserGrowthStatsDto> GetUserGrowthStatsAsync(DateTime? from, DateTime? to);
    Task<RevenueStatsDto> GetRevenueStatsAsync(string? groupBy, DateTime? from, DateTime? to);
    Task<List<CategoryPopularityDto>> GetCategoryPopularityAsync(int limit, string sort);
    Task<FinancialReportDto> GetFinancialReportAsync(DateTime? from, DateTime? to, string? groupBy);
    Task<OrganizerEventSummaryDto> GetOrganizerEventSummaryAsync(Guid eventId);
    Task<List<AiChatTopicTrendDto>> GetAiChatTrendsAsync();
}
