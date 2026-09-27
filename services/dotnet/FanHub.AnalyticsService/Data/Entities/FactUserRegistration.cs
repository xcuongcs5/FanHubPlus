using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FanHub.AnalyticsService.Data.Entities;

[Table("fact_user_registrations")]
public class FactUserRegistration
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(255)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Role { get; set; } = "User";

    public bool IsBanned { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? BannedAt { get; set; }
}
