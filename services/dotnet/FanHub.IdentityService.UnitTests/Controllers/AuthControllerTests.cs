using System.Security.Claims;
using FanHub.IdentityService.Controllers;
using FanHub.IdentityService.Models.DTOs.Auth;
using FanHub.IdentityService.Models.DTOs.Profile;
using FanHub.IdentityService.Services;
using FanHub.Shared.Common.Responses;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace FanHub.IdentityService.UnitTests.Controllers;

public class AuthControllerTests
{
    private readonly Mock<IAuthService> _mockAuthService;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _mockAuthService = new Mock<IAuthService>();
        _controller = new AuthController(_mockAuthService.Object);
    }

    private void SetupUser(Guid userId)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, "mock"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task Register_ShouldReturnStatusCode_FromServiceResult()
    {
        var request = new RegisterRequest();
        var response = ApiResponse<AuthResponse>.Created(new AuthResponse(), "Success");
        _mockAuthService.Setup(x => x.RegisterAsync(request)).ReturnsAsync(response);

        var result = await _controller.Register(request) as ObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(201);
        result.Value.Should().BeEquivalentTo(response);
    }

    [Fact]
    public async Task Login_ShouldReturnStatusCode_FromServiceResult()
    {
        var request = new LoginRequest();
        var response = ApiResponse<AuthResponse>.Ok(new AuthResponse(), "Success");
        _mockAuthService.Setup(x => x.LoginAsync(request)).ReturnsAsync(response);

        var result = await _controller.Login(request) as ObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
        result.Value.Should().BeEquivalentTo(response);
    }

    [Fact]
    public async Task Logout_ShouldCallService_WithCorrectUserId()
    {
        var userId = Guid.NewGuid();
        SetupUser(userId);
        var request = new RefreshTokenRequest { RefreshToken = "token" };
        var response = ApiResponse<object>.Ok(string.Empty, "Success");
        _mockAuthService.Setup(x => x.LogoutAsync("token", userId)).ReturnsAsync(response);

        var result = await _controller.Logout(request) as ObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
        _mockAuthService.Verify(x => x.LogoutAsync("token", userId), Times.Once);
    }

    [Fact]
    public async Task GetMe_ShouldCallService_WithCorrectUserId()
    {
        var userId = Guid.NewGuid();
        SetupUser(userId);
        var response = ApiResponse<UserProfileResponse>.Ok(new UserProfileResponse());
        _mockAuthService.Setup(x => x.GetProfileAsync(userId)).ReturnsAsync(response);

        var result = await _controller.GetMe() as ObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
        _mockAuthService.Verify(x => x.GetProfileAsync(userId), Times.Once);
    }

    [Fact]
    public async Task ForgotPassword_ShouldCallService()
    {
        var request = new ForgotPasswordRequest();
        var response = ApiResponse<object>.Ok(string.Empty);
        _mockAuthService.Setup(x => x.ForgotPasswordAsync(request)).ReturnsAsync(response);
        var result = await _controller.ForgotPassword(request) as ObjectResult;
        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ResetPassword_ShouldCallService()
    {
        var request = new ResetPasswordRequest();
        var response = ApiResponse<object>.Ok(string.Empty);
        _mockAuthService.Setup(x => x.ResetPasswordAsync(request)).ReturnsAsync(response);
        var result = await _controller.ResetPassword(request) as ObjectResult;
        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task RefreshToken_ShouldCallService()
    {
        var request = new RefreshTokenRequest();
        var response = ApiResponse<AuthResponse>.Ok(new AuthResponse());
        _mockAuthService.Setup(x => x.RefreshTokenAsync(request)).ReturnsAsync(response);
        var result = await _controller.RefreshToken(request) as ObjectResult;
        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
    }
}
