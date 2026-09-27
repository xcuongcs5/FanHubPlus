using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FanHub.AnalyticsService.Data.Entities;

[Table("fact_chat_interactions")]
public class FactChatInteraction
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? UserId { get; set; }

    [MaxLength(100)]
    public string SessionId { get; set; } = string.Empty;

    public string UserQuery { get; set; } = string.Empty;

    public string? BotResponse { get; set; }

    public bool IsResolved { get; set; } = true;

    [Column(TypeName = "decimal(5,4)")]
    public decimal ToxicScore { get; set; } = 0;

    [MaxLength(100)]
    public string? Topic { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
