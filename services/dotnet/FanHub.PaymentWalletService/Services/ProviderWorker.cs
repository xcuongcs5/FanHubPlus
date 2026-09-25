using FanHub.PaymentWalletService.Data;
using FanHub.PaymentWalletService.Providers;
using FanHub.Shared.Contracts.Events;
using Microsoft.EntityFrameworkCore;

namespace FanHub.PaymentWalletService.Services;

public sealed class ProviderWorker(IServiceScopeFactory scopes, ILogger<ProviderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogWarning("Provider worker failed ({ErrorType}); reconciliation will resume.", e.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
    public async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PaymentService>();
        var gateway = scope.ServiceProvider.GetRequiredService<IVnPayGateway>();
        // Commit Processing before the network call. A crash/timeout must not cause another refund submission.
        var candidate = await db.Refunds.AsNoTracking().Where(x => x.Status == "Pending" && x.Transaction.Provider == "VNPay")
            .OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (candidate is not null)
        {
            var now = DateTime.UtcNow;
            var claimed = await db.Refunds.Where(x => x.RefundId == candidate.RefundId && x.Status == "Pending")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Processing").SetProperty(x => x.AttemptedAt, now)
                    .SetProperty(x => x.NextQueryAt, now.AddMinutes(5)).SetProperty(x => x.LastError, "awaiting_confirmation"), ct);
            if (claimed == 1)
            {
                candidate.AttemptedAt = now;
                var payment = await db.Transactions.AsNoTracking().SingleAsync(x => x.TransactionId == candidate.TransactionId, ct);
                try
                {
                    var result = await gateway.RefundAsync(payment, candidate, ct);
                    await ApplyRefundAsync(db, service, candidate.RefundId, result, ct);
                }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
                {
                    logger.LogWarning("Refund {RefundId} outcome is unknown; retained for reconciliation.", candidate.RefundId);
                }
            }
        }
        db.ChangeTracker.Clear();
        // querydr is read-only and safe to retry. A conditional lease limits calls across replicas.
        var due = DateTime.UtcNow;
        var pending = await db.Transactions.AsNoTracking().Where(x => x.Provider == "VNPay" &&
            (x.Status == "Pending" || x.Status == "Failed") && x.QueryAttempts < 100 && x.NextQueryAt <= due)
            .OrderBy(x => x.NextQueryAt).Take(10).ToListAsync(ct);
        foreach (var p in pending)
        {
            var claimed = await db.Transactions.Where(x => x.TransactionId == p.TransactionId && x.NextQueryAt <= due)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextQueryAt, due.AddMinutes(5)).SetProperty(x => x.QueryAttempts, x => x.QueryAttempts + 1), ct);
            if (claimed == 0) continue;
            try
            {
                var result = await gateway.QueryAsync(p, ct);
                if (result.Verified && result.Status is "Succeeded" or "Failed")
                    await service.SettleAsync("VNPay", new Settlement(p.MerchantReference, result.ProviderId!, p.Amount,
                        result.Status == "Succeeded", PaymentService.Hash(result)), ct);
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
            { logger.LogWarning("Payment {TransactionId} reconciliation will retry.", p.TransactionId); }
            db.ChangeTracker.Clear();
        }
        var refunds = await db.Refunds.AsNoTracking().Where(x => x.Status == "Processing" && x.NextQueryAt <= due && x.Transaction.Provider == "VNPay")
            .OrderBy(x => x.NextQueryAt).Take(10).ToListAsync(ct);
        foreach (var refund in refunds)
        {
            var claimed = await db.Refunds.Where(x => x.RefundId == refund.RefundId && x.Status == "Processing" && x.NextQueryAt <= due)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextQueryAt, due.AddMinutes(30)), ct);
            if (claimed == 0) continue;
            var p = await db.Transactions.AsNoTracking().SingleAsync(x => x.TransactionId == refund.TransactionId, ct);
            try
            {
                var result = await gateway.QueryAsync(p, ct);
                // A successful ORIGINAL payment does not prove its refund completed.
                if (result.Verified && result.Status == "Refunded")
                    await ApplyRefundAsync(db, service, refund.RefundId, result with { Status = "Succeeded" }, ct);
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
            { logger.LogWarning("Refund {RefundId} requires further reconciliation.", refund.RefundId); }
            db.ChangeTracker.Clear();
        }
    }
    private static async Task ApplyRefundAsync(PaymentDbContext db, PaymentService service, Guid id, ProviderResult result, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await service.LockAsync($"refund:{id}", ct);
        var refund = await db.Refunds.Include(x => x.Transaction).SingleAsync(x => x.RefundId == id, ct);
        if (refund.Status != "Processing") return;
        if (result.Verified && result.Status is "Succeeded" or "Failed")
        {
            refund.Status = result.Status; refund.CompletedAt = DateTime.UtcNow; refund.ProviderRefundId = result.ProviderId;
            refund.LastError = null; refund.NextQueryAt = null;
            service.Emit(refund.RefundId, 1, new BookingRefundResultEvent(refund.TransactionId, refund.Transaction.BookingId!.Value, result.Status == "Succeeded"));
        }
        else refund.LastError = result.Verified ? "provider_processing_or_unknown" : "unverified_response";
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
