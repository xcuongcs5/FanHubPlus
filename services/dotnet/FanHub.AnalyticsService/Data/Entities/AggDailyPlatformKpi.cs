using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FanHub.AnalyticsService.Data.Entities;

[Table("agg_daily_platform_kpis")]
public class AggDailyPlatformKpi
{
    [Key]
    public int Id { get; set; }

    public DateOnly Date { get; set; }

    public int NewUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int TotalTicketsSold { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalGmv { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalCommission { get; set; }

    public int TotalEventsCreated { get; set; }
    public int TotalPostsCreated { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
