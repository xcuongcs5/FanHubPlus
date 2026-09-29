using System.Text.Json.Serialization;

namespace FanHub.AnalyticsService.DTOs;

public class OrganizerEventSummaryDto
{
    [JsonPropertyName("event_id")]
    public Guid EventId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("total_tickets_reserved")]
    public int TotalTicketsReserved { get; set; }

    [JsonPropertyName("total_tickets_sold")]
    public int TotalTicketsSold { get; set; }

    [JsonPropertyName("total_revenue")]
    public decimal TotalRevenue { get; set; }

    [JsonPropertyName("capacity")]
    public int Capacity { get; set; }

    [JsonPropertyName("sold_out_rate")]
    public string SoldOutRate { get; set; } = "0%";

    [JsonPropertyName("total_checked_in")]
    public int TotalCheckedIn { get; set; }

    [JsonPropertyName("attendance_rate")]
    public string AttendanceRate { get; set; } = "0%";
}

public class TelemetryItemDto
{
    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty; // page_view, event_view, button_click, search

    [JsonPropertyName("target_id")]
    public string? TargetId { get; set; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("user_id")]
    public Guid? UserId { get; set; }

    [JsonPropertyName("metadata")]
    public string? Metadata { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }
}

public class TelemetryBatchRequestDto
{
    [JsonPropertyName("events")]
    public List<TelemetryItemDto> Events { get; set; } = new();
}

public class AiChatTopicTrendDto
{
    [JsonPropertyName("topic")]
    public string Topic { get; set; } = string.Empty;

    [JsonPropertyName("query_count")]
    public int QueryCount { get; set; }

    [JsonPropertyName("unresolved_count")]
    public int UnresolvedCount { get; set; }

    [JsonPropertyName("avg_toxic_score")]
    public decimal AvgToxicScore { get; set; }
}

public class AiRecordInteractionDto
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public Guid? UserId { get; set; }

    [JsonPropertyName("user_query")]
    public string UserQuery { get; set; } = string.Empty;

    [JsonPropertyName("bot_response")]
    public string? BotResponse { get; set; }

    [JsonPropertyName("is_resolved")]
    public bool IsResolved { get; set; } = true;

    [JsonPropertyName("toxic_score")]
    public decimal ToxicScore { get; set; } = 0;

    [JsonPropertyName("topic")]
    public string? Topic { get; set; }
}
