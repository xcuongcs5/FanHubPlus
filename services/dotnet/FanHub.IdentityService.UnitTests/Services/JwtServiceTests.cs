using System;
using System.Collections.Generic;
using FanHub.IdentityService.Models.Entities;
using FanHub.IdentityService.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;
using System.IdentityModel.Tokens.Jwt;

namespace FanHub.IdentityService.UnitTests.Services;

public class JwtServiceTests
{
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly JwtService _sut;

    public JwtServiceTests()
    {
        _configurationMock = new Mock<IConfiguration>();
        _configurationMock.Setup(c => c["Jwt:Secret"]).Returns("FanHubPlus-Super-Secret-Key-2026-Must-Be-At-Least-32-Characters-Long!");
        _configurationMock.Setup(c => c["Jwt:Issuer"]).Returns("FanHub.IdentityService");
        _configurationMock.Setup(c => c["Jwt:Audience"]).Returns("FanHub.Services");
        _configurationMock.Setup(c => c["Jwt:ExpiresInMinutes"]).Returns("60");

        _sut = new JwtService(_configurationMock.Object);
    }

    [Fact]
    public void GenerateAccessToken_ShouldReturnValidJwtToken()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "test@fanhub.com",
            FullName = "Test User"
        };
        var roles = new List<string> { "User", "Admin" };

        // Act
        var token = _sut.GenerateAccessToken(user, roles);

        // Assert
        token.Should().NotBeNullOrEmpty();
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);
        
        jwtToken.Issuer.Should().Be("FanHub.IdentityService");
        jwtToken.Audiences.Should().Contain("FanHub.Services");
        jwtToken.Claims.Should().Contain(c => c.Type == System.Security.Claims.ClaimTypes.Role && c.Value == "Admin");
    }

    [Fact]
    public void GenerateRefreshToken_ShouldReturnBase64String()
    {
        // Act
        var token = _sut.GenerateRefreshToken();

        // Assert
        token.Should().NotBeNullOrEmpty();
        Action decode = () => Convert.FromBase64String(token);
        decode.Should().NotThrow();
    }

    [Fact]
    public void ValidateAccessToken_WithValidToken_ShouldReturnUserId()
    {
        // Arrange
        var user = new User { Id = Guid.NewGuid(), Email = "test@fanhub.com", FullName = "Test" };
        var token = _sut.GenerateAccessToken(user, new List<string>());

        // Act
        var result = _sut.ValidateAccessToken(token);

        // Assert
        result.Should().Be(user.Id);
    }

    [Fact]
    public void ValidateAccessToken_WithInvalidToken_ShouldReturnNull()
    {
        // Act
        var result = _sut.ValidateAccessToken("invalid_token_string");

        // Assert
        result.Should().BeNull();
    }
}
