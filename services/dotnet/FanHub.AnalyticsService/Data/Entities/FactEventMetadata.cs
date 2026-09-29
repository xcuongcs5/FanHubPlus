using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FanHub.AnalyticsService.Data.Entities;

[Table("fact_event_metadata")]
public class FactEventMetadata
{
    [Key]
    public Guid EventId { get; set; }

    public Guid OrganizerId { get; set; }

    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    public int Capacity { get; set; }

    [MaxLength(50)]
    public string Status { get; set; } = "Draft";

    public bool IsFeatured { get; set; }

    [MaxLength(255)]
    public string LocationName { get; set; } = string.Empty;

    public string CategoryIdsJson { get; set; } = "[]";

    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}
