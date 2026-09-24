using FanHub.IdentityService.Data;
using FanHub.IdentityService.Models.DTOs.Auth;
using FanHub.IdentityService.Models.DTOs.Profile;
using FanHub.IdentityService.Models.Entities;
using FanHub.Shared.Common.Responses;
using FanHub.Shared.Contracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace FanHub.IdentityService.Services;

public class AuthService : IAuthService
{
    private readonly AuthDbContext _context;
    private readonly IJwtService _jwtService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AuthDbContext context,
        IJwtService jwtService,
        IPublishEndpoint publishEndpoint,
        ILogger<AuthService> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<ApiResponse<AuthResponse>> RegisterAsync(RegisterRequest request)
    {
        // Check if email already exists
        if (await _context.Users.AnyAsync(u => u.Email == request.Email.ToLower()))
            return ApiResponse<AuthResponse>.Fail("Email đã được sử dụng", 409);

        // Create user
        var user = new User
        {
            Email = request.Email.ToLower().Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FullName = request.FullName.Trim(),
            PhoneNumber = request.PhoneNumber?.Trim(),
            EmailVerificationToken = Guid.NewGuid().ToString("N"),
            EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24)
        };

        // Assign default "User" role
        var userRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "User");
        if (userRole != null)
        {
            user.UserRoles.Add(new UserRole { RoleId = userRole.Id });
        }

        // Create default preference
        user.Preference = new UserPreference();

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Publish UserCreated event to RabbitMQ
        await _publishEndpoint.Publish(new UserCreatedEvent
        {
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            AvatarUrl = user.AvatarUrl,
            CreatedAt = user.CreatedAt
        });

        _logger.LogInformation("User registered: {Email}", user.Email);

        // Generate tokens
        var roles = user.UserRoles.Select(ur => ur.Role?.Name ?? "User").ToList();
        var accessToken = _jwtService.GenerateAccessToken(user, roles);
        var refreshToken = _jwtService.GenerateRefreshToken();

        // Save refresh token
        _context.RefreshTokens.Add(new RefreshToken
        {
            Token = refreshToken,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        await _context.SaveChangesAsync();

        var response = new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(60),
            User = new UserInfoResponse
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                AvatarUrl = user.AvatarUrl,
                Roles = roles
            }
        };

        return ApiResponse<AuthResponse>.Created(response, "Đăng ký thành công");
    }

    public async Task<ApiResponse<AuthResponse>> LoginAsync(LoginRequest request)
    {
        var user = await _context.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == request.Email.ToLower());

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return ApiResponse<AuthResponse>.Fail("Email hoặc mật khẩu không đúng", 401);

        if (user.Status == "Banned")
            return ApiResponse<AuthResponse>.Fail("Tài khoản đã bị khóa", 403);

        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        var accessToken = _jwtService.GenerateAccessToken(user, roles);
        var refreshToken = _jwtService.GenerateRefreshToken();

        // Save refresh token
        _context.RefreshTokens.Add(new RefreshToken
        {
            Token = refreshToken,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        await _context.SaveChangesAsync();

        _logger.LogInformation("User logged in: {Email}", user.Email);

        return ApiResponse<AuthResponse>.Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(60),
            User = new UserInfoResponse
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                AvatarUrl = user.AvatarUrl,
                Roles = roles
            }
        }, "Đăng nhập thành công");
    }

    public async Task<ApiResponse<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request)
    {
        var storedToken = await _context.RefreshTokens
            .Include(rt => rt.User).ThenInclude(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

        if (storedToken == null)
            return ApiResponse<AuthResponse>.Fail("Refresh token không hợp lệ", 401);

        if (storedToken.IsRevoked)
            return ApiResponse<AuthResponse>.Fail("Refresh token đã bị thu hồi", 401);

        if (storedToken.ExpiresAt < DateTime.UtcNow)
            return ApiResponse<AuthResponse>.Fail("Refresh token đã hết hạn", 401);

        // Revoke old token
        storedToken.IsRevoked = true;
        storedToken.RevokedAt = DateTime.UtcNow;

        var user = storedToken.User;
        var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();

        // Generate new tokens
        var newAccessToken = _jwtService.GenerateAccessToken(user, roles);
        var newRefreshToken = _jwtService.GenerateRefreshToken();

        storedToken.ReplacedByToken = newRefreshToken;

        _context.RefreshTokens.Add(new RefreshToken
        {
            Token = newRefreshToken,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });
        await _context.SaveChangesAsync();

        return ApiResponse<AuthResponse>.Ok(new AuthResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(60),
            User = new UserInfoResponse
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                AvatarUrl = user.AvatarUrl,
                Roles = roles
            }
        }, "Làm mới token thành công");
    }

    public async Task<ApiResponse<object>> LogoutAsync(string refreshToken, Guid userId)
    {
        var storedToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken && rt.UserId == userId);

        if (storedToken != null)
        {
            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return ApiResponse<object>.Ok(null!, "Đăng xuất thành công");
    }

    public async Task<ApiResponse<object>> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email.ToLower());

        // Always return success to prevent email enumeration
        if (user == null)
            return ApiResponse<object>.Ok(null!, "Nếu email tồn tại, bạn sẽ nhận được link đặt lại mật khẩu");

        user.PasswordResetToken = Guid.NewGuid().ToString("N");
        user.PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(1);
        await _context.SaveChangesAsync();

        // TODO: Send email with reset link via Notification Service
        _logger.LogInformation("Password reset token generated for: {Email}, Token: {Token}", 
            user.Email, user.PasswordResetToken);

        return ApiResponse<object>.Ok(null!, "Nếu email tồn tại, bạn sẽ nhận được link đặt lại mật khẩu");
    }

    public async Task<ApiResponse<object>> ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.PasswordResetToken == request.Token 
                                  && u.PasswordResetTokenExpiry > DateTime.UtcNow);

        if (user == null)
            return ApiResponse<object>.Fail("Token không hợp lệ hoặc đã hết hạn");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiry = null;

        // Revoke all refresh tokens
        var tokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == user.Id && !rt.IsRevoked)
            .ToListAsync();
        foreach (var token in tokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return ApiResponse<object>.Ok(null!, "Đặt lại mật khẩu thành công");
    }

    public async Task<ApiResponse<UserProfileResponse>> GetProfileAsync(Guid userId)
    {
        var user = await _context.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.Preference)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return ApiResponse<UserProfileResponse>.NotFound("Không tìm thấy người dùng");

        var profile = new UserProfileResponse
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            AvatarUrl = user.AvatarUrl,
            PhoneNumber = user.PhoneNumber,
            IsEmailVerified = user.IsEmailVerified,
            Roles = user.UserRoles.Select(ur => ur.Role.Name).ToList(),
            CreatedAt = user.CreatedAt,
            Preference = user.Preference != null ? new UserPreferenceResponse
            {
                Theme = user.Preference.Theme,
                NotificationEnabled = user.Preference.NotificationEnabled,
                FavoriteFandoms = user.Preference.FavoriteFandoms,
                Language = user.Preference.Language
            } : null
        };

        return ApiResponse<UserProfileResponse>.Ok(profile);
    }

    public async Task<ApiResponse<UserProfileResponse>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var user = await _context.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.Preference)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return ApiResponse<UserProfileResponse>.NotFound("Không tìm thấy người dùng");

        // Update user info
        if (request.FullName != null) user.FullName = request.FullName.Trim();
        if (request.AvatarUrl != null) user.AvatarUrl = request.AvatarUrl;
        if (request.PhoneNumber != null) user.PhoneNumber = request.PhoneNumber.Trim();

        // Update preferences
        if (user.Preference == null)
            user.Preference = new UserPreference { UserId = user.Id };

        if (request.Theme != null) user.Preference.Theme = request.Theme;
        if (request.NotificationEnabled.HasValue) user.Preference.NotificationEnabled = request.NotificationEnabled.Value;
        if (request.FavoriteFandoms != null) user.Preference.FavoriteFandoms = request.FavoriteFandoms;
        if (request.Language != null) user.Preference.Language = request.Language;

        await _context.SaveChangesAsync();

        // Publish UserUpdated event
        await _publishEndpoint.Publish(new UserUpdatedEvent
        {
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            AvatarUrl = user.AvatarUrl,
            UpdatedAt = DateTime.UtcNow
        });

        return await GetProfileAsync(userId);
    }

    public async Task<ApiResponse<object>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return ApiResponse<object>.NotFound("Không tìm thấy người dùng");

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return ApiResponse<object>.Fail("Mật khẩu hiện tại không đúng");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _context.SaveChangesAsync();

        return ApiResponse<object>.Ok(null!, "Đổi mật khẩu thành công");
    }

    public async Task<ApiResponse<object>> VerifyEmailAsync(VerifyEmailRequest request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.EmailVerificationToken == request.Token
                                  && u.EmailVerificationTokenExpiry > DateTime.UtcNow);

        if (user == null)
            return ApiResponse<object>.Fail("Token xác thực không hợp lệ hoặc đã hết hạn");

        user.IsEmailVerified = true;
        user.EmailVerificationToken = null;
        user.EmailVerificationTokenExpiry = null;
        await _context.SaveChangesAsync();

        return ApiResponse<object>.Ok(null!, "Xác thực email thành công");
    }
}
