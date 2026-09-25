using System.Diagnostics.Metrics;
using FanHub.NotificationService.Data;
using FanHub.NotificationService.Services;
using Microsoft.EntityFrameworkCore;

namespace FanHub.NotificationService.Push;

public sealed class DeliveryWorker(IServiceScopeFactory scopes, ILogger<DeliveryWorker> logger) : BackgroundService
{
    private static readonly Meter Meter = new("FanHub.NotificationService");
    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>("notification.push.outcomes");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { if (!await RunOneAsync(stoppingToken)) await Task.Delay(1000, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Exception messages from providers/SQL may contain tokens. Log the type only.
                logger.LogError("Push worker failed ({ErrorType}); retrying.", ex.GetType().Name);
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
    public async Task<bool> RunOneAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<NotificationStore>();
        var now = DateTime.UtcNow;
        await db.Deliveries.Where(x => (x.Status == "Pending" || x.Status == "Processing") &&
            (x.LockedUntil == null || x.LockedUntil < now) && (x.ExpiresAt <= now || x.AttemptCount >= 12))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Failed").SetProperty(x => x.LastError, "delivery_expired_or_exhausted")
                .SetProperty(x => x.LockId, (Guid?)null).SetProperty(x => x.LockedUntil, (DateTime?)null), ct);
        var candidate = await db.Deliveries.AsNoTracking().Where(x => (x.Status == "Pending" || x.Status == "Processing") &&
            x.NextAttemptAt <= now && x.ExpiresAt > now && x.AttemptCount < 12 && (x.LockedUntil == null || x.LockedUntil < now))
            .OrderBy(x => x.NextAttemptAt).ThenBy(x => x.DeliveryId).FirstOrDefaultAsync(ct);
        if (candidate is null) return false;
        var lease = Guid.NewGuid();
        var count = await db.Deliveries.Where(x => x.DeliveryId == candidate.DeliveryId &&
            (x.Status == "Pending" || x.Status == "Processing") && (x.LockedUntil == null || x.LockedUntil < now))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LockId, lease).SetProperty(x => x.LockedUntil, now.AddMinutes(2))
                .SetProperty(x => x.Status, "Processing").SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), ct);
        if (count == 0) return true;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await store.LockAsync($"device:{candidate.DeviceTokenId}", ct);
        var item = await db.Deliveries.SingleAsync(x => x.DeliveryId == candidate.DeliveryId, ct);
        if (item.LockId != lease || item.Status != "Processing") return true;
        var token = await db.Tokens.FindAsync([candidate.DeviceTokenId], ct);
        var notification = await db.Notifications.FindAsync([candidate.NotificationId], ct);
        PushResult result;
        if (token is null || notification is null || !token.IsActive || token.Provider != "FCM" ||
            token.UserId != notification.UserId || token.BindingId != item.BindingId || token.LastSeenAt < now.AddDays(-30) ||
            await db.Users.AnyAsync(x => x.UserId == notification.UserId && x.Status != "Active", ct))
            result = new("Skipped", Error: "inactive_or_changed_recipient");
        else
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try { result = await scope.ServiceProvider.GetRequiredService<IPushSender>().SendAsync(token.Token, notification.NotificationId, timeout.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { result = new("Retry", Error: "Timeout"); }
        }
        item.LockId = null; item.LockedUntil = null; item.LastError = result.Error;
        switch (result.Outcome)
        {
            case "Sent": item.Status = "Sent"; item.SentAt = DateTime.UtcNow; item.ProviderMessageId = result.MessageId; break;
            case "Skipped": item.Status = "Skipped"; break;
            case "InvalidToken": token!.IsActive = false; item.Status = "Failed"; break;
            case "Permanent": item.Status = "Failed"; break;
            default:
                item.Status = item.AttemptCount >= 12 ? "Failed" : "Pending";
                item.NextAttemptAt = DateTime.UtcNow.AddSeconds(Math.Min(3600, 60 * Math.Pow(2, Math.Min(item.AttemptCount - 1, 6))) + Random.Shared.Next(1, 30));
                break;
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        Outcomes.Add(1, new KeyValuePair<string, object?>("outcome", result.Outcome));
        if (result.Outcome is not ("Sent" or "Skipped")) logger.LogWarning("Push delivery {DeliveryId}: {Outcome}, code {Code}", item.DeliveryId, result.Outcome, result.Error);
        return true;
    }
}
