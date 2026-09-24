using FanHub.Shared.Common.Models;

namespace FanHub.IdentityService.Models.Entities;

public class UserPreference : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Theme { get; set; } = "light";
    public bool NotificationEnabled { get; set; } = true;
    public string? FavoriteFandoms { get; set; } // JSON array
    public string Language { get; set; } = "vi";
}
