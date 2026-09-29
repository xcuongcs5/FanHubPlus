using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FanHub.AnalyticsService.Data.Entities;

[Table("fact_ticket_sales")]
public class FactTicketSale
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BookingId { get; set; }
    public Guid EventId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TicketTypeId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [MaxLength(10)]
    public string Currency { get; set; } = "VND";

    [MaxLength(50)]
    public string Status { get; set; } = "Reserved"; // Reserved, Paid, Cancelled, Refunded

    [Column(TypeName = "decimal(18,2)")]
    public decimal CommissionAmount { get; set; } = 0;

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public DateTime? RefundedAt { get; set; }
}
