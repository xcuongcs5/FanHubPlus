using FanHub.IdentityService.Models.DTOs.Auth;
using FanHub.IdentityService.Models.DTOs.Profile;
using FanHub.Shared.Common.Responses;

namespace FanHub.IdentityService.Services;

public interface IAuthService
{
    Task<ApiResponse<AuthResponse>> RegisterAsync(RegisterRequest request);
    Task<ApiResponse<AuthResponse>> LoginAsync(LoginRequest request);
    Task<ApiResponse<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request);
    Task<ApiResponse<object>> LogoutAsync(string refreshToken, Guid userId);
    Task<ApiResponse<object>> ForgotPasswordAsync(ForgotPasswordRequest request);
    Task<ApiResponse<object>> ResetPasswordAsync(ResetPasswordRequest request);
    Task<ApiResponse<UserProfileResponse>> GetProfileAsync(Guid userId);
    Task<ApiResponse<UserProfileResponse>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request);
    Task<ApiResponse<object>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
    Task<ApiResponse<object>> VerifyEmailAsync(VerifyEmailRequest request);
}
