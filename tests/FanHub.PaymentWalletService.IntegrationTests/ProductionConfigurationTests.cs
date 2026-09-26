using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FanHub.PaymentWalletService.IntegrationTests;

public sealed class ProductionConfigurationTests
{
    [Theory]
    [InlineData("Encrypt=False;TrustServerCertificate=False", "false", "true", "Production SQL")]
    [InlineData("Encrypt=True;TrustServerCertificate=True", "true", "true", "Production SQL")]
    [InlineData("Encrypt=True;TrustServerCertificate=False", "false", "true", "Production requires RabbitMQ")]
    [InlineData("Encrypt=True;TrustServerCertificate=False", "true", "false", "Production requires provider")]
    [InlineData("Encrypt=True;TrustServerCertificate=False", "true", "true", "Sandbox credentials")]
    public void Unsafe_production_configuration_fails_before_connecting(string sql, string tls, string worker, string expected)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:PaymentDb", "Server=example.invalid;Database=test;Integrated Security=True;" + sql);
            builder.UseSetting("Jwt:Secret", "configuration-tests-only-12345678901234567890");
            builder.UseSetting("RabbitMq:UseTls", tls);
            builder.UseSetting("ProviderWorker:Enabled", worker);
            builder.UseSetting("VnPay:Enabled", "true");
            builder.UseSetting("VnPay:Sandbox", "true");
            builder.UseSetting("VnPay:TmnCode", "TESTCODE");
            builder.UseSetting("VnPay:HashSecret", PaymentFixture.ProviderSecret);
            builder.UseSetting("VnPay:ReturnUrl", "https://example.invalid/return");
        });
        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(expected, failure.ToString());
    }
}
