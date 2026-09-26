using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FanHub.NotificationService.Api;
using FanHub.NotificationService.Data;
using FanHub.NotificationService.Messaging;
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
var connection = builder.Configuration.GetConnectionString("NotificationDb") ?? throw new InvalidOperationException("ConnectionStrings:NotificationDb is required.");
var secret = builder.Configuration["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret is required.");
if (Encoding.UTF8.GetByteCount(secret) < 32) throw new InvalidOperationException("JWT signing key must contain at least 32 UTF-8 bytes.");
var production = !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing");
if (production)
{
    var sql = new SqlConnectionStringBuilder(connection);
    if (sql.TrustServerCertificate || sql.Encrypt == SqlConnectionEncryptOption.Optional)
        throw new InvalidOperationException("Production SQL requires encryption and certificate validation.");
    if (!builder.Configuration.GetValue("Messaging:Enabled", true) || !builder.Configuration.GetValue("RabbitMq:UseTls", false))
        throw new InvalidOperationException("Production requires RabbitMQ messaging with TLS.");
}
builder.Services.AddDbContext<NotificationDbContext>(options => options.UseSqlServer(connection));
builder.Services.AddScoped<FanHub.NotificationService.Services.NotificationStore>();

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
            var db = context.HttpContext.RequestServices.GetRequiredService<NotificationDbContext>();
            if (await db.Users.AnyAsync(x => x.UserId == id && x.Status != "Active", context.HttpContext.RequestAborted))
                context.Fail("Account is inactive.");
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "FanHub Notification Service — tasks 51–54", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
    });
});

if (builder.Configuration.GetValue("Messaging:Enabled", true))
{
    builder.Services.AddMassTransit(bus =>
    {
        bus.AddConsumer<NotificationConsumer<NotificationRequestedEvent>>();
        bus.AddConsumer<NotificationConsumer<BookingChangedEvent>>();
        bus.AddConsumer<NotificationConsumer<UserCreatedEvent>>();
        bus.AddConsumer<NotificationConsumer<UserUpdatedEvent>>();
        bus.AddConsumer<NotificationConsumer<UserBannedEvent>>();
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
            var prefix = builder.Configuration["RabbitMq:QueuePrefix"] ?? "fanhub-notification";
            rabbit.ReceiveEndpoint($"{prefix}-projections", endpoint =>
            {
                endpoint.PrefetchCount = 16;
                endpoint.UseMessageRetry(retry => { retry.Ignore<ArgumentException>(); retry.Intervals(500, 1000, 3000); });
                endpoint.ConfigureConsumer<NotificationConsumer<NotificationRequestedEvent>>(context);
                endpoint.ConfigureConsumer<NotificationConsumer<BookingChangedEvent>>(context);
                endpoint.ConfigureConsumer<NotificationConsumer<UserCreatedEvent>>(context);
                endpoint.ConfigureConsumer<NotificationConsumer<UserUpdatedEvent>>(context);
                endpoint.ConfigureConsumer<NotificationConsumer<UserBannedEvent>>(context);
            });
        });
    });

}

var pushEnabled = builder.Configuration.GetValue("Firebase:Enabled", false);
if (production && !pushEnabled)
    throw new InvalidOperationException("Firebase:Enabled must be true outside Development/Testing.");
if (pushEnabled)
{
    var project = builder.Configuration["Firebase:ProjectId"];
    if (string.IsNullOrWhiteSpace(project)) throw new InvalidOperationException("Firebase:ProjectId is required.");
    var firebase = FirebaseAdmin.FirebaseApp.Create(new FirebaseAdmin.AppOptions
    {
        Credential = await Google.Apis.Auth.OAuth2.GoogleCredential.GetApplicationDefaultAsync(), ProjectId = project
    }, "fanhub-notification");
    builder.Services.AddSingleton(firebase);
    builder.Services.AddSingleton(FirebaseAdmin.Messaging.FirebaseMessaging.GetMessaging(firebase));
    builder.Services.AddSingleton<FanHub.NotificationService.Push.IPushSender, FanHub.NotificationService.Push.FirebasePushSender>();
    builder.Services.AddHostedService<FanHub.NotificationService.Push.DeliveryWorker>();
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
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { code, message, trace_id = context.TraceIdentifier });
    }
});
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/health/live", () => Results.Ok(new { service = "FanHub.NotificationService", status = "Alive" })).ExcludeFromDescription();
app.MapGet("/health/ready", async (NotificationDbContext db, Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService health, CancellationToken ct) =>
{
    await db.Deliveries.Select(x => x.BindingId).Take(1).ToListAsync(ct);
    var report = await health.CheckHealthAsync(ct);
    if (report.Status != Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy)
        return Results.Json(new { status = "NotReady" }, statusCode: 503);
    return Results.Ok(new { status = "Ready", push_enabled = pushEnabled });
}).ExcludeFromDescription();
app.Run();

public partial class Program;

public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDateTime().ToUniversalTime();
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
