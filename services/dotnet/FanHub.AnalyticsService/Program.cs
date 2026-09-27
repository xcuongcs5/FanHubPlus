using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FanHub.AnalyticsService.Consumers;
using FanHub.AnalyticsService.Data;
using FanHub.AnalyticsService.Services;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;

if (args.Contains("--health-check"))
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try
    {
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080";
        Environment.Exit((await client.GetAsync($"http://127.0.0.1:{port}/health/ready")).IsSuccessStatusCode ? 0 : 1);
    }
    catch { Environment.Exit(1); }
    return;
}

var builder = WebApplication.CreateBuilder(args);

// 1. Database Configuration (SQL Server with In-Memory fallback for test/dev flexibility)
var dbConn = builder.Configuration.GetConnectionString("AnalyticsDb");
if (!string.IsNullOrWhiteSpace(dbConn))
{
    builder.Services.AddDbContext<AnalyticsDbContext>(options =>
        options.UseSqlServer(dbConn, sql => sql.EnableRetryOnFailure(3)));
}
else
{
    builder.Services.AddDbContext<AnalyticsDbContext>(options =>
        options.UseInMemoryDatabase("FanHubAnalyticsDb"));
}

// 2. Redis Configuration
var redisConn = builder.Configuration.GetConnectionString("Redis")
    ?? builder.Configuration["Redis:ConnectionString"];

if (!string.IsNullOrWhiteSpace(redisConn))
{
    try
    {
        var redisMux = ConnectionMultiplexer.Connect(redisConn);
        builder.Services.AddSingleton<IConnectionMultiplexer>(redisMux);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Warning] Cannot connect to Redis: {ex.Message}. Falling back to in-memory mode.");
    }
}

// 3. Domain Services
builder.Services.AddSingleton<IRedisMetricsService, RedisMetricsService>();
builder.Services.AddScoped<IAnalyticsQueryService, AnalyticsQueryService>();
builder.Services.AddScoped<ITelemetryService, TelemetryService>();

// 4. Controllers & JSON serialization
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// 5. JWT Authentication (Optional validation, allows admin inspection)
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "fanhubplus_super_secret_development_key_2026_min32bytes!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();

// 6. Swagger OpenAPI
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FanHub Analytics & Reporting Service — Tasks 65-68 & 103",
        Version = "v1",
        Description = "Microservice chịu trách nhiệm tổng hợp chỉ số, phân tích doanh thu, tăng trưởng người dùng, fandom và dữ liệu AI Chatbot."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập Access Token JWT"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// 7. MassTransit & RabbitMQ Consumers
if (builder.Configuration.GetValue("Messaging:Enabled", true))
{
    builder.Services.AddMassTransit(bus =>
    {
        bus.AddConsumer<AnalyticsEventConsumers>();

        bus.UsingRabbitMq((context, rabbit) =>
        {
            var host = builder.Configuration["RabbitMq:Host"] ?? "localhost";
            var port = ushort.Parse(builder.Configuration["RabbitMq:Port"] ?? "5673");
            var vhost = builder.Configuration["RabbitMq:VirtualHost"] ?? "fanhub";
            var username = builder.Configuration["RabbitMq:Username"] ?? "fanhub";
            var password = builder.Configuration["RabbitMq:Password"] ?? "fanhub";

            rabbit.Host(host, port, vhost, h =>
            {
                h.Username(username);
                h.Password(password);
            });

            var queuePrefix = builder.Configuration["RabbitMq:QueuePrefix"] ?? "fanhub-analytics";
            rabbit.ReceiveEndpoint($"{queuePrefix}-events", endpoint =>
            {
                endpoint.PrefetchCount = 16;
                endpoint.UseMessageRetry(r => r.Intervals(500, 1000, 2000));
                endpoint.ConfigureConsumer<AnalyticsEventConsumers>(context);
            });
        });
    });
}

// 8. CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// Ensure DB Created if using Relational or InMemory
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
    try
    {
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not EnsureCreated on Database. Check connection string.");
    }
}

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "FanHub.AnalyticsService v1");
    c.RoutePrefix = "swagger";
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => Results.Ok(new
{
    service = "FanHub.AnalyticsService",
    status = "Healthy",
    version = "1.0.0",
    docs = "/swagger"
}));

app.MapGet("/health/ready", () => Results.Ok(new { status = "Ready" }));

app.Run();
