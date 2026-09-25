using FanHub.PaymentWalletService.Data;
using FanHub.PaymentWalletService.Providers;
using FanHub.PaymentWalletService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FanHub.PaymentWalletService.IntegrationTests;

public sealed class RefundWorkerTests(PaymentFixture fixture) : IClassFixture<PaymentFixture>
{
    [Fact]
    public async Task Ambiguous_refund_is_never_resubmitted_and_only_confirmed_refund_publishes_success()
    {
        var fake = new FakeGateway();
        await using var services = new ServiceCollection().AddDbContext<PaymentDbContext>(o => o.UseSqlServer(fixture.Connection))
            .AddSingleton<IVnPayGateway>(fake).AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddScoped<PaymentService>().BuildServiceProvider();
        var worker = new ProviderWorker(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ProviderWorker>.Instance);
        var user = Guid.NewGuid(); var booking = Guid.NewGuid();
        var p = new PaymentTransaction { UserId = user, BookingId = booking, Purpose = "Booking", Provider = "VNPay",
            MerchantReference = Guid.NewGuid().ToString("N"), IdempotencyKey = Guid.NewGuid().ToString("N"), RequestHash = new byte[32],
            Amount = 20000, Status = "Succeeded", ExpiresAt = DateTime.UtcNow.AddMinutes(10), ProviderTransactionId = "12345" };
        var refund = new PaymentRefund { TransactionId = p.TransactionId, RequestedBy = user, Amount = p.Amount, Reason = "cancelled", IdempotencyKey = "refund" };
        await using (var db = fixture.Db())
        {
            db.Bookings.Add(new BookingProjection { BookingId = booking, UserId = user, EventId = Guid.NewGuid(), Amount = p.Amount,
                Status = "RefundPending", ExpiresAt = p.ExpiresAt });
            db.Transactions.Add(p); db.Refunds.Add(refund); await db.SaveChangesAsync();
        }
        await Task.WhenAll(worker.TickAsync(default), worker.TickAsync(default));
        Assert.Equal(1, fake.RefundCalls);
        await using (var db = fixture.Db())
        {
            var r = await db.Refunds.FindAsync(refund.RefundId); Assert.Equal("Processing", r!.Status); Assert.Null(r.CompletedAt);
            Assert.False(await db.Outbox.AnyAsync(x => x.AggregateId == refund.RefundId));
            r.NextQueryAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        }
        fake.QueryStatus = "Succeeded"; // Original payment success must not mark refund paid.
        await worker.TickAsync(default);
        await using (var db = fixture.Db())
        {
            var r = await db.Refunds.FindAsync(refund.RefundId); Assert.Equal("Processing", r!.Status);
            r.NextQueryAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        }
        fake.QueryStatus = "Refunded";
        await worker.TickAsync(default); await worker.TickAsync(default);
        Assert.Equal(1, fake.RefundCalls);
        await using var final = fixture.Db();
        Assert.Equal("Succeeded", (await final.Refunds.FindAsync(refund.RefundId))!.Status);
        Assert.Equal(1, await final.Outbox.CountAsync(x => x.AggregateId == refund.RefundId));
    }
    private sealed class FakeGateway : IVnPayGateway
    {
        public int RefundCalls;
        public string QueryStatus = "Unknown";
        public string Checkout(PaymentTransaction p, string ip) => throw new NotSupportedException();
        public Settlement? Verify(IReadOnlyDictionary<string, string> f) => throw new NotSupportedException();
        public Task<ProviderResult> QueryAsync(PaymentTransaction p, CancellationToken ct) => Task.FromResult(new ProviderResult(true, QueryStatus, "99999"));
        public Task<ProviderResult> RefundAsync(PaymentTransaction p, PaymentRefund r, CancellationToken ct)
        {
            Interlocked.Increment(ref RefundCalls);
            throw new HttpRequestException("Simulated connection lost after provider accepted refund.");
        }
    }
}
