namespace FanHub.IdentityService.Models.DTOs.Auth;

public class VerifyEmailRequest
{
    public string Token { get; set; } = string.Empty;
}
