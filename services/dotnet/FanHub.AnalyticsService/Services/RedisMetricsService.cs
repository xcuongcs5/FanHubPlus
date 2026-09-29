using System.Text.Json;
using StackExchange.Redis;

namespace FanHub.AnalyticsService.Services;

public class RedisMetricsService : IRedisMetricsService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<RedisMetricsService> _logger;

    public RedisMetricsService(ILogger<RedisMetricsService> logger, IConnectionMultiplexer? redis = null)
    {
        _logger = logger;
        _redis = redis;
    }

    public async Task RecordUserActiveAsync(Guid userId)
    {
        if (_redis == null || !_redis.IsConnected) return;

        try
        {
            var db = _redis.GetDatabase();
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            var month = DateTime.UtcNow.ToString("yyyyMM");

            var dauKey = $"fanhub:analytics:dau:{today}";
            var mauKey = $"fanhub:analytics:mau:{month}";

            await db.HyperLogLogAddAsync(dauKey, userId.ToString());
            await db.KeyExpireAsync(dauKey, TimeSpan.FromDays(7));

            await db.HyperLogLogAddAsync(mauKey, userId.ToString());
            await db.KeyExpireAsync(mauKey, TimeSpan.FromDays(60));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record active user in Redis");
        }
    }

    public async Task<int> GetDailyActiveUsersAsync(DateOnly? date = null)
    {
        if (_redis == null || !_redis.IsConnected) return 0;

        try
        {
            var target = (date ?? DateOnly.FromDateTime(DateTime.UtcNow)).ToString("yyyyMMdd");
            var dauKey = $"fanhub:analytics:dau:{target}";
            var db = _redis.GetDatabase();
            var count = await db.HyperLogLogLengthAsync(dauKey);
            return (int)count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve DAU from Redis");
            return 0;
        }
    }

    public async Task<int> GetMonthlyActiveUsersAsync(int? year = null, int? month = null)
    {
        if (_redis == null || !_redis.IsConnected) return 0;

        try
        {
            var now = DateTime.UtcNow;
            var targetYear = year ?? now.Year;
            var targetMonth = month ?? now.Month;
            var mauKey = $"fanhub:analytics:mau:{targetYear:D4}{targetMonth:D2}";
            var db = _redis.GetDatabase();
            var count = await db.HyperLogLogLengthAsync(mauKey);
            return (int)count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve MAU from Redis");
            return 0;
        }
    }

    public async Task<T?> GetCachedAsync<T>(string key)
    {
        if (_redis == null || !_redis.IsConnected) return default;

        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(key);
            if (!value.HasValue) return default;
            return JsonSerializer.Deserialize<T>((string)value!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get cache for key {Key}", key);
            return default;
        }
    }

    public async Task SetCachedAsync<T>(string key, T value, TimeSpan? ttl = null)
    {
        if (_redis == null || !_redis.IsConnected) return;

        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(value);
            await db.StringSetAsync(key, json, ttl ?? TimeSpan.FromMinutes(5));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set cache for key {Key}", key);
        }
    }
}
