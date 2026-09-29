using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FanHub.AnalyticsService.Data.Entities;

[Table("fact_event_attendance")]
public class FactEventAttendance
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BookingId { get; set; }
    public Guid EventId { get; set; }
    public Guid UserId { get; set; }

    [MaxLength(50)]
    public string Status { get; set; } = "CheckedIn";

    public DateTime CheckedInAt { get; set; } = DateTime.UtcNow;
}
