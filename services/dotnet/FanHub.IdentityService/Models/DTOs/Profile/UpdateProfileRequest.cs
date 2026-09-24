namespace FanHub.IdentityService.Models.DTOs.Profile;

public class UpdateProfileRequest
{
    public string? FullName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Theme { get; set; }
    public bool? NotificationEnabled { get; set; }
    public string? FavoriteFandoms { get; set; }
    public string? Language { get; set; }
}
