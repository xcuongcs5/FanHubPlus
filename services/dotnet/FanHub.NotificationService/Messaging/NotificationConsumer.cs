using FanHub.NotificationService.Data;
using FanHub.NotificationService.Services;
using FanHub.Shared.Contracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace FanHub.NotificationService.Messaging;

public sealed class NotificationConsumer<T>(NotificationDbContext db, NotificationStore store, FanHub.NotificationService.Services.IEmailSender emailSender, Microsoft.Extensions.Configuration.IConfiguration config) : IConsumer<T> where T : class
{
    public async Task Consume(ConsumeContext<T> context)
    {
        var id = context.MessageId ?? throw new ArgumentException("MessageId required.");
        if (id == Guid.Empty) throw new ArgumentException("MessageId required.");
        var consumer = typeof(T).Name;
        await using var tx = await db.Database.BeginTransactionAsync(context.CancellationToken);
        var ct = context.CancellationToken;
        await store.LockAsync($"inbox:{consumer}:{id}", ct);
        if (await db.Inbox.AnyAsync(x => x.Consumer == consumer && x.MessageId == id, ct)) return;
        switch (context.Message)
        {
            case NotificationRequestedEvent m: await store.CreateAsync(id, m, ct); break;
            case BookingChangedEvent m:
                if (m.BookingId == Guid.Empty || m.UserId == Guid.Empty || m.SourceVersion <= 0) throw new ArgumentException("Invalid booking snapshot.");
                await store.LockAsync($"booking:{m.BookingId}", ct);
                var previous = await db.BookingStates.FindAsync([m.BookingId], ct);
                if (previous is not null && previous.SourceVersion >= m.SourceVersion) break;
                var changed = previous is null || previous.Status != m.Status || previous.UserId != m.UserId;
                if (previous is null) { previous = new BookingNotificationState { BookingId = m.BookingId }; db.BookingStates.Add(previous); }
                previous.UserId = m.UserId; previous.Status = m.Status; previous.SourceVersion = m.SourceVersion;
                if (changed && m.Status is "Active" or "Cancelled" or "Expired" or "RefundPending" or "Refunded" or "CheckedIn")
                {
                    await store.CreateAsync(id, new NotificationRequestedEvent(m.UserId, "Booking." + m.Status, "Cập nhật vé FanHub",
                        $"Trạng thái vé của bạn: {m.Status}.", new() { ["booking_id"] = m.BookingId.ToString(), ["event_id"] = m.EventId.ToString(), ["status"] = m.Status }), ct);
                    
                    if (m.Status == "Active")
                    {
                        var user = await db.Users.FindAsync([m.UserId], ct);
                        string userEmail = !string.IsNullOrEmpty(user?.Email) ? user.Email : (config["TEST_TARGET_EMAIL"] ?? config["Smtp:Username"] ?? "fanhub.demo@gmail.com");
                        string userName = user?.FullName ?? "bạn";
                        
                        string body = $@"
                        <h2>Xin chào {userName},</h2>
                        <p>Vé sự kiện của bạn đã được đúc thành công trên hệ thống Blockchain.</p>
                        <p>Mã vé (Booking ID): {m.BookingId}</p>
                        <p>Vui lòng giữ lại email này để check-in tại sự kiện.</p>
                        <br/>
                        <p>Cảm ơn bạn đã sử dụng FanHubPlus!</p>";
                        
                        try {
                            await emailSender.SendEmailAsync(userEmail, "FanHub - Đặt vé thành công!", body);
                        } catch (Exception ex) {
                            Console.WriteLine("Error sending email: " + ex.Message);
                        }
                    }
                }
                break;
            case UserCreatedEvent m: await UserAsync(m.UserId, m.Email, m.FullName, m.AvatarUrl, m.CreatedAt, false, ct); break;
            case UserUpdatedEvent m: await UserAsync(m.UserId, m.Email, m.FullName, m.AvatarUrl, m.UpdatedAt, false, ct); break;
            case UserBannedEvent m: await UserAsync(m.UserId, null, null, null, m.BannedAt, true, ct); break;
        }
        db.Inbox.Add(new InboxMessage { Consumer = consumer, MessageId = id });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private async Task UserAsync(Guid id, string? email, string? name, string? avatar, DateTime timestamp, bool banned, CancellationToken ct)
    {
        if (id == Guid.Empty || timestamp == default || name?.Length > 200 || avatar?.Length > 500) throw new ArgumentException("Invalid user snapshot.");
        await store.LockAsync($"user:{id}", ct);
        var item = await db.Users.FindAsync([id], ct);
        if (item is not null && (item.SourceUpdatedAt > timestamp || item.SourceUpdatedAt == timestamp && !banned)) return;
        if (item is null) { item = new UserProjection { UserId = id, Status = "Active" }; db.Users.Add(item); }
        if (name is not null) { item.FullName = name; item.AvatarUrl = avatar; }
        if (email is not null) item.Email = email;
        if (banned) item.Status = "Banned";
        item.SourceUpdatedAt = timestamp; item.SyncedAt = DateTime.UtcNow;
    }
}
