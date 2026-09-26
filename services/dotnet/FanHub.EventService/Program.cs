using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FanHub.EventService.Api;
using FanHub.EventService.Data;
using FanHub.EventService.Messaging;
using MassTransit;
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
var connection = builder.Configuration.GetConnectionString("EventDb") ?? throw new InvalidOperationException("ConnectionStrings:EventDb is required.");
var secret = builder.Configuration["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret is required.");
if (Encoding.UTF8.GetByteCount(secret) < 32) throw new InvalidOperationException("JWT signing key must contain at least 32 UTF-8 bytes.");
builder.Services.AddDbContext<EventDbContext>(options => options.UseSqlServer(connection));
builder.Services.AddScoped<FanHub.EventService.Services.EventService>();
builder.Services.AddScoped<ProjectionHandler>();
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
            var db = context.HttpContext.RequestServices.GetRequiredService<EventDbContext>();
            if (await db.Users.AnyAsync(x => x.UserId == id && x.Status != "Active", context.HttpContext.RequestAborted))
                context.Fail("Account is inactive.");
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "FanHub Event Service — tasks 21–32", Version = "v1" });
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
        bus.AddConsumer<CategoryConsumer>(); bus.AddConsumer<AttendeeConsumer>(); bus.AddConsumer<ReviewConsumer>();
        bus.AddConsumer<UserCreatedConsumer>(); bus.AddConsumer<UserUpdatedConsumer>(); bus.AddConsumer<UserBannedConsumer>();
        bus.UsingRabbitMq((context, rabbit) =>
        {
            rabbit.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost",
                ushort.Parse(builder.Configuration["RabbitMq:Port"] ?? "5673"),
                builder.Configuration["RabbitMq:VirtualHost"] ?? "fanhub", host =>
                {
                    host.Username(builder.Configuration["RabbitMq:Username"] ?? "fanhub");
                    host.Password(builder.Configuration["RabbitMq:Password"] ?? throw new InvalidOperationException("RabbitMq:Password is required."));
                });
            var prefix = builder.Configuration["RabbitMq:QueuePrefix"] ?? "fanhub-event";
            rabbit.ReceiveEndpoint($"{prefix}-projections", endpoint =>
            {
                endpoint.PrefetchCount = 16;
                endpoint.UseMessageRetry(retry => { retry.Ignore<ArgumentException>(); retry.Intervals(500, 1000, 3000); });
                endpoint.ConfigureConsumer<CategoryConsumer>(context); endpoint.ConfigureConsumer<AttendeeConsumer>(context);
                endpoint.ConfigureConsumer<UserCreatedConsumer>(context); endpoint.ConfigureConsumer<UserUpdatedConsumer>(context);
                endpoint.ConfigureConsumer<UserBannedConsumer>(context);
            });
            rabbit.ReceiveEndpoint($"{prefix}-reviews", endpoint =>
            {
                endpoint.UseMessageRetry(retry => { retry.Ignore<ArgumentException>(); retry.Intervals(500, 1000, 3000); });
                endpoint.ConfigureConsumer<ReviewConsumer>(context);
            });
        });
    });
    builder.Services.AddHostedService<OutboxPublisher>();
}

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
        if (status >= 500) app.Logger.LogError(exception, "Request failed: {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { code, message, trace_id = context.TraceIdentifier });
    }
});
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health/live", () => Results.Ok(new { service = "FanHub.EventService", status = "Alive" })).ExcludeFromDescription();
app.MapGet("/health/ready", async (EventDbContext db, CancellationToken ct) =>
{
    await db.Events.OrderBy(x => x.EventId).Select(x => x.ReviewRequestId).Take(1).ToListAsync(ct);
    return Results.Ok(new { status = "Ready" });
}).ExcludeFromDescription();
app.Run();

public partial class Program;

public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDateTime().ToUniversalTime();
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
