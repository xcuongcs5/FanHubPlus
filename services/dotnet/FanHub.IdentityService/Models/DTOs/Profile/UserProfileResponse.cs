namespace FanHub.IdentityService.Models.DTOs.Profile;

public class UserProfileResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsEmailVerified { get; set; }
    public List<string> Roles { get; set; } = new();
    public UserPreferenceResponse? Preference { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserPreferenceResponse
{
    public string Theme { get; set; } = string.Empty;
    public bool NotificationEnabled { get; set; }
    public string? FavoriteFandoms { get; set; }
    public string Language { get; set; } = string.Empty;
}
