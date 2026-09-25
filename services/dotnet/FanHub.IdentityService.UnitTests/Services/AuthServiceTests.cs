using FanHub.IdentityService.Data;
using FanHub.IdentityService.Models.DTOs.Auth;
using FanHub.IdentityService.Models.DTOs.Profile;
using FanHub.IdentityService.Models.Entities;
using FanHub.IdentityService.Services;
using FanHub.Shared.Contracts.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FanHub.IdentityService.UnitTests.Services;

public class AuthServiceTests
{
    private readonly DbContextOptions<AuthDbContext> _dbContextOptions;
    private readonly Mock<IJwtService> _mockJwtService;
    private readonly Mock<IPublishEndpoint> _mockPublishEndpoint;
    private readonly Mock<ILogger<AuthService>> _mockLogger;

    public AuthServiceTests()
    {
        _dbContextOptions = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _mockJwtService = new Mock<IJwtService>();
        _mockPublishEndpoint = new Mock<IPublishEndpoint>();
        _mockLogger = new Mock<ILogger<AuthService>>();
    }

    private AuthService CreateService(AuthDbContext context)
    {
        return new AuthService(context, _mockJwtService.Object, _mockPublishEndpoint.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task RegisterAsync_ShouldReturnConflict_WhenEmailExists()
    {
        using var context = new AuthDbContext(_dbContextOptions);
        context.Users.Add(new User { Email = "test@example.com", PasswordHash = "hash" });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var request = new RegisterRequest { Email = "test@example.com", Password = "password", FullName = "Test User" };

        var result = await service.RegisterAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Message.Should().Be("Email đã được sử dụng");
    }

    [Fact]
    public async Task RegisterAsync_ShouldCreateUserAndTokens_WhenEmailDoesNotExist()
    {
        using var context = new AuthDbContext(_dbContextOptions);
        context.Roles.Add(new Role { Name = "User" });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var request = new RegisterRequest { Email = "new@example.com", Password = "password", FullName = "New User" };

        _mockJwtService.Setup(x => x.GenerateAccessToken(It.IsAny<User>(), It.IsAny<List<string>>())).Returns("access_token");
        _mockJwtService.Setup(x => x.GenerateRefreshToken()).Returns("refresh_token");

        var result = await service.RegisterAsync(request);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.AccessToken.Should().Be("access_token");
        result.Data.RefreshToken.Should().Be("refresh_token");
        result.Data.User.Email.Should().Be("new@example.com");

        var userInDb = await context.Users.FirstOrDefaultAsync(u => u.Email == "new@example.com");
        userInDb.Should().NotBeNull();
        userInDb!.FullName.Should().Be("New User");

        _mockPublishEndpoint.Verify(x => x.Publish(It.IsAny<UserCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnUnauthorized_WhenUserNotFound()
    {
        using var context = new AuthDbContext(_dbContextOptions);
        var service = CreateService(context);
        var request = new LoginRequest { Email = "nonexistent@example.com", Password = "password" };

        var result = await service.LoginAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task RefreshTokenAsync_ShouldReturnUnauthorized_WhenTokenNotFound()
    {
        using var context = new AuthDbContext(_dbContextOptions);
        var service = CreateService(context);
        var request = new RefreshTokenRequest { RefreshToken = "invalid_token" };

        var result = await service.RefreshTokenAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        result.Message.Should().Be("Refresh token không hợp lệ");
    }

    [Fact]
    public async Task LogoutAsync_ShouldRevokeToken_WhenTokenExists()
    {
        var userId = Guid.NewGuid();
        using var context = new AuthDbContext(_dbContextOptions);
        context.RefreshTokens.Add(new RefreshToken { Token = "token", UserId = userId, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        
        var result = await service.LogoutAsync("token", userId);

        result.Success.Should().BeTrue();
        var tokenInDb = await context.RefreshTokens.FirstAsync();
        tokenInDb.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task ResetPasswordAsync_ShouldReturnFail_WhenTokenInvalid()
    {
        using var context = new AuthDbContext(_dbContextOptions);
        var service = CreateService(context);
        var request = new ResetPasswordRequest { Token = "invalid", NewPassword = "newpassword" };

        var result = await service.ResetPasswordAsync(request);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ChangePasswordAsync_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        using var context = new AuthDbContext(_dbContextOptions);
        var service = CreateService(context);
        var request = new ChangePasswordRequest { CurrentPassword = "old", NewPassword = "new" };

        var result = await service.ChangePasswordAsync(Guid.NewGuid(), request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
