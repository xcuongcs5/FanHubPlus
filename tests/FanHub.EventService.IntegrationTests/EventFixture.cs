using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FanHub.EventService.Data;
using FanHub.Shared.Contracts.Events;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FanHub.EventService.IntegrationTests;

public sealed class EventFixture : IAsyncLifetime
{
    private readonly string suffix = Guid.NewGuid().ToString("N");
    private readonly string adminConnection = Environment.GetEnvironmentVariable("FANHUB_TEST_SQL_ADMIN")
        ?? throw new InvalidOperationException("Run docker/chinhduc/Test-EventService.ps1 to supply isolated test infrastructure settings.");
    private readonly HttpClient management = new();
    private WebApplicationFactory<Program> factory = null!;
    public IBusControl Bus { get; private set; } = null!;
    public string Connection { get; private set; } = "";
    public string SigningKey { get; } = "Event-tests-only-" + Guid.NewGuid().ToString("N");
    public ConcurrentQueue<(Guid? MessageId, EventChangedEvent Message)> Published { get; } = new();
    private string Database => "fanhub_event_test_" + suffix;
    private string Login => "event_test_" + suffix;
    private string Vhost => "event-test-" + suffix;
    private bool databaseCreated, loginCreated, vhostCreated;

    public async Task InitializeAsync()
    {
        var password = "Test9@" + Guid.NewGuid().ToString("N");
        await SqlAsync(adminConnection, $"CREATE DATABASE [{Database}]");
        databaseCreated = true;
        await SqlAsync(adminConnection, $"CREATE LOGIN [{Login}] WITH PASSWORD=N'{password}'");
        loginCreated = true;
        var admin = new SqlConnectionStringBuilder(adminConnection) { InitialCatalog = Database };
        var root = FindRoot();
        var migrations = Directory.GetFiles(Path.Combine(root, "database/event"), "*.sql")
            .Concat(Directory.GetFiles(Path.Combine(root, "database/common"), "*.sql")).OrderBy(Path.GetFileName);
        foreach (var path in migrations) await SqlAsync(admin.ConnectionString, await File.ReadAllTextAsync(path));
        await SqlAsync(admin.ConnectionString, $"""
            CREATE USER [{Login}] FOR LOGIN [{Login}];
            GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [{Login}];
            """);
        var app = new SqlConnectionStringBuilder(adminConnection) { InitialCatalog = Database, UserID = Login, Password = password, IntegratedSecurity = false };
        Connection = app.ConnectionString;
        var rabbitPassword = Environment.GetEnvironmentVariable("FANHUB_TEST_RABBIT_PASSWORD") ?? throw new InvalidOperationException("Rabbit password missing.");
        var rabbitPort = Environment.GetEnvironmentVariable("FANHUB_TEST_RABBIT_PORT") ?? "5673";
        var managementPort = Environment.GetEnvironmentVariable("FANHUB_TEST_RABBIT_MANAGEMENT_PORT") ?? "15673";
        management.BaseAddress = new Uri($"http://localhost:{managementPort}");
        management.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("fanhub:" + rabbitPassword)));
        (await management.PutAsJsonAsync($"/api/vhosts/{Vhost}", new { })).EnsureSuccessStatusCode();
        vhostCreated = true;
        (await management.PutAsJsonAsync($"/api/permissions/{Vhost}/fanhub", new { configure = ".*", write = ".*", read = ".*" })).EnsureSuccessStatusCode();
        Bus = MassTransit.Bus.Factory.CreateUsingRabbitMq(config =>
        {
            config.Host("localhost", ushort.Parse(rabbitPort), Vhost, host => { host.Username("fanhub"); host.Password(rabbitPassword); });
            config.ReceiveEndpoint("event-test-observer", endpoint => endpoint.Handler<EventChangedEvent>(context =>
            {
                Published.Enqueue((context.MessageId, context.Message));
                return Task.CompletedTask;
            }));
        });
        await Bus.StartAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:EventDb", Connection);
            builder.UseSetting("Jwt:Secret", SigningKey);
            builder.UseSetting("RabbitMq:Host", "localhost");
            builder.UseSetting("RabbitMq:Port", rabbitPort);
            builder.UseSetting("RabbitMq:VirtualHost", Vhost);
            builder.UseSetting("RabbitMq:Username", "fanhub");
            builder.UseSetting("RabbitMq:Password", rabbitPassword);
            builder.UseSetting("Logging:LogLevel:Default", "Warning");
        });
        using var ready = factory.CreateClient();
        (await ready.GetAsync("/health/ready")).EnsureSuccessStatusCode();
        // Wait for durable receive queues before publishing integration events.
        await EventuallyAsync(async () => (await management.GetAsync($"/api/queues/{Vhost}/fanhub-event-projections")).IsSuccessStatusCode);
    }

    public HttpClient Client(Guid? id = null, string role = "User", string? key = null, DateTime? expiry = null, string? subject = null)
    {
        var client = factory.CreateClient();
        if (id is null && subject is null) return client;
        var token = new JwtSecurityToken("FanHub.IdentityService", "FanHub.Services",
            [new Claim(ClaimTypes.NameIdentifier, subject ?? id!.Value.ToString()), new Claim(ClaimTypes.Role, role)],
            expires: expiry ?? DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? SigningKey)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    public EventDbContext Db() => new(new DbContextOptionsBuilder<EventDbContext>().UseSqlServer(Connection).Options);

    public Task AdminSqlAsync(string sql) => SqlAsync(new SqlConnectionStringBuilder(adminConnection) { InitialCatalog = Database }.ConnectionString, sql);

    public async Task PublishAsync<T>(T message, string consumer, Guid? messageId = null) where T : class
    {
        var id = messageId ?? Guid.NewGuid();
        await Bus.Publish(message, context => context.MessageId = id);
        await EventuallyAsync(async () => { await using var db = Db(); return await db.Inbox.AnyAsync(x => x.MessageId == id && x.Consumer == consumer); });
    }

    public static async Task EventuallyAsync(Func<Task<bool>> check)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < until) { if (await check()) return; await Task.Delay(100); }
        Assert.True(await check(), "Expected eventual state was not reached within 20 seconds.");
    }

    private static async Task SqlAsync(string connection, string sql)
    {
        await using var db = new SqlConnection(connection);
        await db.OpenAsync();
        await using var command = db.CreateCommand();
        command.CommandText = "SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; " + sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string FindRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "FanHubPlus.slnx"))) folder = folder.Parent;
        return folder?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
        if (Bus is not null) await Bus.StopAsync();
        if (vhostCreated) (await management.DeleteAsync($"/api/vhosts/{Vhost}")).EnsureSuccessStatusCode();
        management.Dispose();
        SqlConnection.ClearAllPools();
        // Names are generated by this fixture, never supplied by the caller.
        if (databaseCreated) await SqlAsync(adminConnection, $"ALTER DATABASE [{Database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Database}];");
        if (loginCreated) await SqlAsync(adminConnection, $"DROP LOGIN [{Login}];");
    }
}
