using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FanHub.NotificationService.IntegrationTests;

public sealed class ProductionConfigurationTests
{
    [Theory]
    [InlineData("Encrypt=False;TrustServerCertificate=False", "false", "Production SQL")]
    [InlineData("Encrypt=True;TrustServerCertificate=True", "true", "Production SQL")]
    [InlineData("Encrypt=True;TrustServerCertificate=False", "false", "Production requires RabbitMQ")]
    public void Insecure_production_configuration_fails_before_connecting(string sql, string tls, string expected)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:NotificationDb", "Server=example.invalid;Database=test;Integrated Security=True;" + sql);
            builder.UseSetting("Jwt:Secret", "configuration-tests-only-12345678901234567890");
            builder.UseSetting("RabbitMq:UseTls", tls);
        });
        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(expected, failure.ToString());
    }
}
