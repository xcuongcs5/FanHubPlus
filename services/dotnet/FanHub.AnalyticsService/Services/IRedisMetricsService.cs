namespace FanHub.AnalyticsService.Services;

public interface IRedisMetricsService
{
    Task RecordUserActiveAsync(Guid userId);
    Task<int> GetDailyActiveUsersAsync(DateOnly? date = null);
    Task<int> GetMonthlyActiveUsersAsync(int? year = null, int? month = null);
    Task<T?> GetCachedAsync<T>(string key);
    Task SetCachedAsync<T>(string key, T value, TimeSpan? ttl = null);
}
