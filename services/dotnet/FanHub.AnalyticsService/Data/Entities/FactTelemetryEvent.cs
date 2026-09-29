using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FanHub.AnalyticsService.Data.Entities;

[Table("fact_telemetry_events")]
public class FactTelemetryEvent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? UserId { get; set; }

    [MaxLength(100)]
    public string? SessionId { get; set; }

    [MaxLength(100)]
    public string EventType { get; set; } = string.Empty; // page_view, event_click, category_view, search

    [MaxLength(255)]
    public string? TargetId { get; set; }

    public string? MetadataJson { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
