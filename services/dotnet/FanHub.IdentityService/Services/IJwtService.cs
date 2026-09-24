using FanHub.IdentityService.Models.Entities;

namespace FanHub.IdentityService.Services;

public interface IJwtService
{
    string GenerateAccessToken(User user, IList<string> roles);
    string GenerateRefreshToken();
    Guid? ValidateAccessToken(string token);
}
