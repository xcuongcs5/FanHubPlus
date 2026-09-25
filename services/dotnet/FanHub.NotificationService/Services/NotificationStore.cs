using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FanHub.NotificationService.Api;
using FanHub.NotificationService.Data;
using FanHub.Shared.Contracts.Events;
using Microsoft.EntityFrameworkCore;

namespace FanHub.NotificationService.Services;

public sealed class NotificationStore(NotificationDbContext db)
{
    public Task<int> LockAsync(string key, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        DECLARE @r int;
        EXEC @r=sys.sp_getapplock @Resource={key}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
        IF @r<0 THROW 51000, 'Notification lock timeout', 1;
        """, ct);
    public async Task<DeviceToken> RegisterAsync(Guid user, DeviceRequest input, CancellationToken ct)
    {
        if (input.DeviceId == Guid.Empty || input.Token.Any(char.IsWhiteSpace) || input.Token.Any(char.IsControl))
            throw new ApiException(400, "invalid_device", "A random installation UUID and a valid FCM token are required.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Small registration critical section also serializes token collisions across device IDs.
        await LockAsync("device-registration", ct);
        var device = input.DeviceId.ToString("D");
        var item = await db.Tokens.SingleOrDefaultAsync(x => x.Provider == "FCM" && x.DeviceId == device, ct);
        if (item is not null)
        {
            await LockAsync($"device:{item.DeviceTokenId}", ct);
            await db.Entry(item).ReloadAsync(ct);
        }
        if (!input.IsActive && (item is null || item.UserId != user || item.Token != input.Token))
            throw new ApiException(404, "not_found", "Device registration not found.");
        var hash = SHA256.HashData(Encoding.Unicode.GetBytes(input.Token));
        // Match SQL's persisted SHA2_256 over nvarchar without comparing tokens under a case-insensitive collation.
        var duplicate = await db.Tokens.FromSqlInterpolated($"SELECT * FROM dbo.DEVICE_TOKEN WHERE provider='FCM' AND token_hash={hash}")
            .SingleOrDefaultAsync(ct);
        if (duplicate is not null && duplicate.DeviceId != device)
            throw new ApiException(409, "token_already_registered", "This token belongs to another installation ID.");
        if (input.IsActive && (item is null || item.UserId != user || !item.IsActive) &&
            await db.Tokens.CountAsync(x => x.UserId == user && x.IsActive, ct) >= 20)
            throw new ApiException(409, "device_limit", "Deactivate an old device before adding another (maximum 20).");
        if (item is null)
        {
            item = new DeviceToken { Provider = "FCM", DeviceId = device, UserId = user, Token = input.Token, IsActive = input.IsActive };
            db.Tokens.Add(item);
        }
        else if (item.UserId != user || item.Token != input.Token || item.IsActive != input.IsActive)
        {
            item.BindingId = Guid.NewGuid(); item.UserId = user; item.Token = input.Token; item.IsActive = input.IsActive;
            await db.Deliveries.Where(x => x.DeviceTokenId == item.DeviceTokenId && (x.Status == "Pending" || x.Status == "Processing"))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Skipped").SetProperty(x => x.LastError, "device_binding_changed")
                    .SetProperty(x => x.LockId, (Guid?)null).SetProperty(x => x.LockedUntil, (DateTime?)null), ct);
        }
        item.LastSeenAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return item;
    }
    public async Task CreateAsync(Guid messageId, NotificationRequestedEvent message, CancellationToken ct)
    {
        if (message.UserId == Guid.Empty || string.IsNullOrWhiteSpace(message.Type) || message.Type.Length > 100 ||
            string.IsNullOrWhiteSpace(message.Title) || message.Title.Length > 250 || string.IsNullOrWhiteSpace(message.Message) || message.Message.Length > 4000)
            throw new ArgumentException("Invalid notification message.");
        var data = message.Data is null ? null : JsonSerializer.Serialize(message.Data);
        if (data?.Length > 8000) throw new ArgumentException("Notification data is too large.");
        if (await db.Notifications.AnyAsync(x => x.UserId == message.UserId && x.SourceMessageId == messageId && x.Type == message.Type, ct)) return;
        if (await db.Users.AnyAsync(x => x.UserId == message.UserId && x.Status != "Active", ct)) return;
        var item = new Notification { UserId = message.UserId, SourceMessageId = messageId, Type = message.Type,
            Title = message.Title, Message = message.Message, DataJson = data };
        db.Notifications.Add(item); await db.SaveChangesAsync(ct);
        var since = DateTime.UtcNow.AddDays(-30);
        var devices = await db.Tokens.AsNoTracking().Where(x => x.UserId == message.UserId && x.IsActive && x.Provider == "FCM" && x.LastSeenAt >= since).ToListAsync(ct);
        foreach (var device in devices) db.Deliveries.Add(new NotificationDelivery { NotificationId = item.NotificationId,
            DeviceTokenId = device.DeviceTokenId, BindingId = device.BindingId, Status = "Pending", ExpiresAt = DateTime.UtcNow.AddDays(1) });
    }
}
