using System.Text.Json.Serialization;

namespace FanHub.AnalyticsService.DTOs;

public class AdminOverviewDto
{
    [JsonPropertyName("total_users")]
    public int TotalUsers { get; set; }

    [JsonPropertyName("active_users")]
    public int ActiveUsers { get; set; }

    [JsonPropertyName("total_events")]
    public int TotalEvents { get; set; }

    [JsonPropertyName("pending_events")]
    public int PendingEvents { get; set; }

    [JsonPropertyName("total_revenue")]
    public decimal TotalRevenue { get; set; }

    [JsonPropertyName("total_posts")]
    public int TotalPosts { get; set; }
}

public class UserGrowthPointDto
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("new_users")]
    public int NewUsers { get; set; }

    [JsonPropertyName("active_users")]
    public int ActiveUsers { get; set; }
}

public class UserGrowthStatsDto
{
    [JsonPropertyName("growth_rate")]
    public string GrowthRate { get; set; } = "0%";

    [JsonPropertyName("chart_data")]
    public List<UserGrowthPointDto> ChartData { get; set; } = new();
}

public class RevenueSeriesPointDto
{
    [JsonPropertyName("period")]
    public string Period { get; set; } = string.Empty;

    [JsonPropertyName("revenue")]
    public decimal Revenue { get; set; }
}

public class RevenueStatsDto
{
    [JsonPropertyName("total_gmv")]
    public decimal TotalGmv { get; set; }

    [JsonPropertyName("total_commission")]
    public decimal TotalCommission { get; set; }

    [JsonPropertyName("series")]
    public List<RevenueSeriesPointDto> Series { get; set; } = new();
}

public class CategoryPopularityDto
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("views")]
    public long Views { get; set; }

    [JsonPropertyName("posts")]
    public int Posts { get; set; }
}

public class FinancialBreakdownItemDto
{
    [JsonPropertyName("event_id")]
    public Guid EventId { get; set; }

    [JsonPropertyName("event_title")]
    public string EventTitle { get; set; } = string.Empty;

    [JsonPropertyName("tickets_sold")]
    public int TicketsSold { get; set; }

    [JsonPropertyName("revenue")]
    public decimal Revenue { get; set; }
}

public class FinancialReportDto
{
    [JsonPropertyName("total_volume")]
    public decimal TotalVolume { get; set; }

    [JsonPropertyName("commission_earned")]
    public decimal CommissionEarned { get; set; }

    [JsonPropertyName("breakdown")]
    public List<FinancialBreakdownItemDto> Breakdown { get; set; } = new();
}
