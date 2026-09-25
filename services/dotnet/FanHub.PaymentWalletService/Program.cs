using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FanHub.PaymentWalletService.Api;
using FanHub.PaymentWalletService.Data;
using FanHub.PaymentWalletService.Messaging;
using MassTransit;
using Microsoft.AspNetCore.RateLimiting;
using FanHub.Shared.Contracts.Events;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

if (args.Contains("--health-check"))
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try { Environment.Exit((await client.GetAsync("http://127.0.0.1:8080/health/ready")).IsSuccessStatusCode ? 0 : 1); }
    catch { Environment.Exit(1); }
    return;
}

var builder = WebApplication.CreateBuilder(args);
// Docker mounts a read-only JSON secret; it is not part of the image or tracked settings.
var providerConfigFile = builder.Configuration["ProviderConfigFile"];
if (!string.IsNullOrWhiteSpace(providerConfigFile)) builder.Configuration.AddJsonFile(providerConfigFile, optional: false, reloadOnChange: false);
var connection = builder.Configuration.GetConnectionString("PaymentDb") ?? throw new InvalidOperationException("ConnectionStrings:PaymentDb is required.");
var secret = builder.Configuration["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret is required.");
if (Encoding.UTF8.GetByteCount(secret) < 32) throw new InvalidOperationException("JWT signing key must contain at least 32 UTF-8 bytes.");
var production = !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing");
if (production)
{
    if (!builder.Configuration.GetValue("ProviderWorker:Enabled", true)) throw new InvalidOperationException("Production requires provider reconciliation workers.");
    var sql = new SqlConnectionStringBuilder(connection);
    if (sql.TrustServerCertificate || sql.Encrypt == SqlConnectionEncryptOption.Optional)
        throw new InvalidOperationException("Production SQL requires encryption and certificate validation.");
    if (!builder.Configuration.GetValue("Messaging:Enabled", true) || !builder.Configuration.GetValue("RabbitMq:UseTls", false))
        throw new InvalidOperationException("Production requires RabbitMQ messaging with TLS.");
}
builder.Services.AddDbContext<PaymentDbContext>(options => options.UseSqlServer(connection));
builder.Services.AddScoped<FanHub.PaymentWalletService.Services.PaymentService>();
builder.Services.AddScoped<MessageHandler>();
builder.Services.AddSingleton<FanHub.PaymentWalletService.Providers.IVnPayGateway, FanHub.PaymentWalletService.Providers.VnPay>();
builder.Services.AddHttpClient("provider", client => { client.Timeout = TimeSpan.FromSeconds(20); client.MaxResponseContentBufferSize = 65536; })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
if (builder.Configuration.GetValue("VnPay:Enabled", false))
{
    var merchant = builder.Configuration["VnPay:TmnCode"] ?? "";
    var hashSecret = builder.Configuration["VnPay:HashSecret"] ?? "";
    if (!System.Text.RegularExpressions.Regex.IsMatch(merchant, "^[A-Za-z0-9]{8}$") || hashSecret.Length < 32)
        throw new InvalidOperationException("Valid VNPAY merchant and signing secret required.");
    var sandbox = builder.Configuration.GetValue("VnPay:Sandbox", true);
    var host = sandbox ? "sandbox.vnpayment.vn" : "pay.vnpay.vn";
    foreach (var key in new[] { "PaymentUrl", "ApiUrl" })
        if (!Uri.TryCreate(builder.Configuration["VnPay:" + key], UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != host || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
            throw new InvalidOperationException("Invalid VNPAY endpoint configuration.");
    if (!Uri.TryCreate(builder.Configuration["VnPay:ReturnUrl"], UriKind.Absolute, out var returnUrl) ||
        (returnUrl.Scheme != "https" && !(returnUrl.Scheme == "http" && returnUrl.IsLoopback && !production)) || returnUrl.ToString().Length > 255)
        throw new InvalidOperationException("VNPAY ReturnUrl must be HTTPS (localhost allowed for Development).");
    if (production && sandbox) throw new InvalidOperationException("Sandbox credentials cannot run in Production.");
    if (!System.Net.IPAddress.TryParse(builder.Configuration["VnPay:ServerIp"], out _))
        throw new InvalidOperationException("VnPay:ServerIp is required for query/refund calls.");
    if (builder.Configuration.GetValue("ProviderWorker:Enabled", true)) builder.Services.AddHostedService<FanHub.PaymentWalletService.Services.ProviderWorker>();
}
else if (production) throw new InvalidOperationException("VNPAY must be configured in Production.");
if (builder.Configuration.GetValue("MoMo:Enabled", false) && new[] { "PartnerCode", "AccessKey", "SecretKey" }.Any(k => string.IsNullOrWhiteSpace(builder.Configuration["MoMo:" + k])))
    throw new InvalidOperationException("MoMo credentials required when enabled.");

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
        ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "FanHub.IdentityService",
        ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"] ?? "FanHub.Services",
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], NameClaimType = ClaimTypes.Name, RoleClaimType = ClaimTypes.Role
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            if (!Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id == Guid.Empty)
            { context.Fail("A valid user subject is required."); return; }
            var db = context.HttpContext.RequestServices.GetRequiredService<PaymentDbContext>();
            if (await db.Users.AnyAsync(x => x.UserId == id && x.Status != "Active", context.HttpContext.RequestAborted))
                context.Fail("Account is inactive.");
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear(); options.KnownProxies.Clear();
    // Trust only explicitly configured proxies. An arbitrary X-Forwarded-For must not control payment IPs.
    foreach (var address in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(System.Net.IPAddress.Parse(address));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "FanHub Payment Wallet Service — tasks 43–50", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
    });
});

if (builder.Configuration.GetValue("Messaging:Enabled", true))
{
    builder.Services.AddHostedService<OutboxPublisher>();
    builder.Services.AddMassTransit(bus =>
    {
        bus.AddConsumer<PaymentConsumer<BookingCreatedEvent>>();
        bus.AddConsumer<PaymentConsumer<UpgradeRequestedEvent>>();
        bus.AddConsumer<PaymentConsumer<BookingRefundRequestedEvent>>();
        bus.AddConsumer<PaymentConsumer<BookingChangedEvent>>();
        bus.AddConsumer<PaymentConsumer<UserCreatedEvent>>();
        bus.AddConsumer<PaymentConsumer<UserUpdatedEvent>>();
        bus.AddConsumer<PaymentConsumer<UserBannedEvent>>();
        bus.UsingRabbitMq((context, rabbit) =>
        {
            rabbit.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost",
                ushort.Parse(builder.Configuration["RabbitMq:Port"] ?? "5673"),
                builder.Configuration["RabbitMq:VirtualHost"] ?? "fanhub", host =>
                {
                    host.Username(builder.Configuration["RabbitMq:Username"] ?? "fanhub");
                    host.Password(builder.Configuration["RabbitMq:Password"] ?? throw new InvalidOperationException("RabbitMq:Password is required."));
                    if (builder.Configuration.GetValue("RabbitMq:UseTls", false)) host.UseSsl(ssl =>
                    {
                        ssl.ServerName = builder.Configuration["RabbitMq:Host"]!;
                        ssl.Protocol = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
                    });
                });
            var prefix = builder.Configuration["RabbitMq:QueuePrefix"] ?? "fanhub-payment";
            rabbit.ReceiveEndpoint($"{prefix}-projections", endpoint =>
            {
                endpoint.PrefetchCount = 16;
                endpoint.UseMessageRetry(retry => { retry.Ignore<ArgumentException>(); retry.Intervals(500, 1000, 3000); });
                endpoint.ConfigureConsumer<PaymentConsumer<BookingCreatedEvent>>(context);
                endpoint.ConfigureConsumer<PaymentConsumer<UpgradeRequestedEvent>>(context);
                endpoint.ConfigureConsumer<PaymentConsumer<BookingRefundRequestedEvent>>(context);
                endpoint.ConfigureConsumer<PaymentConsumer<BookingChangedEvent>>(context);
                endpoint.ConfigureConsumer<PaymentConsumer<UserCreatedEvent>>(context);
                endpoint.ConfigureConsumer<PaymentConsumer<UserUpdatedEvent>>(context);
                endpoint.ConfigureConsumer<PaymentConsumer<UserBannedEvent>>(context);
            });
        });
    });

}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("user", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (Exception exception) when (!context.Response.HasStarted)
    {
        var (status, code, message) = exception switch
        {
            ApiException error => (error.Status, error.Code, error.Message),
            DbUpdateConcurrencyException => (409, "concurrency_conflict", "Record changed. Reload and retry."),
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } => (409, "duplicate_record", "This operation conflicts with an existing record."),
            SqlException => (503, "database_unavailable", "Database is temporarily unavailable."),
            _ => (500, "internal_error", "An unexpected error occurred.")
        };
        if (status >= 500) app.Logger.LogError("Request failed ({ErrorType}): {TraceId}", exception.GetType().Name, context.TraceIdentifier);
        if (context.Request.Path.Equals("/api/v1/payments/webhook/vnpay", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 200;
            await context.Response.WriteAsJsonAsync(new Dictionary<string, string> { ["RspCode"] = "99", ["Message"] = "Retry later" });
            return;
        }
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { code, message, trace_id = context.TraceIdentifier });
    }
});
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
if ((builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? []).Length > 0) app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/health/live", () => Results.Ok(new { service = "FanHub.PaymentWalletService", status = "Alive" })).ExcludeFromDescription();
app.MapGet("/health/ready", async (PaymentDbContext db, Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService health, CancellationToken ct) =>
{
    await db.Transactions.Select(x => x.NextQueryAt).Take(1).ToListAsync(ct);
    var report = await health.CheckHealthAsync(ct);
    if (report.Status != Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy)
        return Results.Json(new { status = "NotReady" }, statusCode: 503);
    return Results.Ok(new { status = "Ready" });
}).ExcludeFromDescription();
app.Run();

public partial class Program;

public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDateTime().ToUniversalTime();
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
