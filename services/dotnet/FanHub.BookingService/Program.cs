using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FanHub.BookingService.Api;
using FanHub.BookingService.Data;
using FanHub.BookingService.Messaging;
using MassTransit;
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
var connection = builder.Configuration.GetConnectionString("BookingDb") ?? throw new InvalidOperationException("ConnectionStrings:BookingDb is required.");
var secret = builder.Configuration["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret is required.");
if (Encoding.UTF8.GetByteCount(secret) < 32) throw new InvalidOperationException("JWT signing key must contain at least 32 UTF-8 bytes.");
builder.Services.AddDbContext<BookingDbContext>(options => options.UseSqlServer(connection));
builder.Services.AddScoped<FanHub.BookingService.Services.BookingService>();
builder.Services.AddScoped<MessageHandler>();
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
            var db = context.HttpContext.RequestServices.GetRequiredService<BookingDbContext>();
            if (await db.Users.AnyAsync(x => x.UserId == id && x.Status != "Active", context.HttpContext.RequestAborted))
                context.Fail("Account is inactive.");
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "FanHub Booking Service — tasks 33–42", Version = "v1" });
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
        bus.AddConsumer<BookingConsumer<EventChangedEvent>>();
        bus.AddConsumer<BookingConsumer<EventStaffChangedEvent>>();
        bus.AddConsumer<BookingConsumer<TicketTypeConfiguredEvent>>();
        bus.AddConsumer<BookingConsumer<ReservationRequestedEvent>>();
        bus.AddConsumer<BookingConsumer<BookingPaymentResultEvent>>();
        bus.AddConsumer<BookingConsumer<BookingRefundResultEvent>>();
        bus.AddConsumer<BookingConsumer<TicketMintResultEvent>>();
        bus.AddConsumer<BookingConsumer<TicketTransferResultEvent>>();
        bus.AddConsumer<BookingConsumer<UserCreatedEvent>>();
        bus.AddConsumer<BookingConsumer<UserUpdatedEvent>>();
        bus.AddConsumer<BookingConsumer<UserBannedEvent>>();
        bus.UsingRabbitMq((context, rabbit) =>
        {
            rabbit.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost",
                ushort.Parse(builder.Configuration["RabbitMq:Port"] ?? "5673"),
                builder.Configuration["RabbitMq:VirtualHost"] ?? "fanhub", host =>
                {
                    host.Username(builder.Configuration["RabbitMq:Username"] ?? "fanhub");
                    host.Password(builder.Configuration["RabbitMq:Password"] ?? throw new InvalidOperationException("RabbitMq:Password is required."));
                });
            var prefix = builder.Configuration["RabbitMq:QueuePrefix"] ?? "fanhub-booking";
            rabbit.ReceiveEndpoint($"{prefix}-projections", endpoint =>
            {
                endpoint.PrefetchCount = 16;
                endpoint.UseMessageRetry(retry => { retry.Ignore<ArgumentException>(); retry.Intervals(500, 1000, 3000); });
                endpoint.ConfigureConsumer<BookingConsumer<EventChangedEvent>>(context);
                endpoint.ConfigureConsumer<BookingConsumer<EventStaffChangedEvent>>(context);
                endpoint.ConfigureConsumer<BookingConsumer<TicketTypeConfiguredEvent>>(context);
                endpoint.ConfigureConsumer<BookingConsumer<ReservationRequestedEvent>>(context);
                endpoint.ConfigureConsumer<BookingConsumer<BookingPaymentResultEvent>>(context);
                endpoint.ConfigureConsumer<BookingConsumer<BookingRefundResultEvent>>(context);
                endpoint.ConfigureConsumer<BookingConsumer<TicketMintResultEvent>>(context);
                endpoint.ConfigureConsumer<BookingConsumer<TicketTransferResultEvent>>(context);
                endpoint.ConfigureConsumer<BookingConsumer<UserCreatedEvent>>(context);
                endpoint.ConfigureConsumer<BookingConsumer<UserUpdatedEvent>>(context);
                endpoint.ConfigureConsumer<BookingConsumer<UserBannedEvent>>(context);
            });
        });
    });
    builder.Services.AddHostedService<OutboxPublisher>();
}

if (builder.Configuration.GetValue("Expiry:Enabled", true)) builder.Services.AddHostedService<FanHub.BookingService.Services.ExpiryWorker>();

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
app.MapGet("/health/live", () => Results.Ok(new { service = "FanHub.BookingService", status = "Alive" })).ExcludeFromDescription();
app.MapGet("/health/ready", async (BookingDbContext db, CancellationToken ct) =>
{
    await db.Tickets.OrderBy(x => x.BookingId).Select(x => x.BlockchainError).Take(1).ToListAsync(ct);
    await db.Payments.Select(x => x.TransactionId).Take(1).ToListAsync(ct);
    return Results.Ok(new { status = "Ready" });
}).ExcludeFromDescription();
app.Run();

public partial class Program;

public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDateTime().ToUniversalTime();
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
