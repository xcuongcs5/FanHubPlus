using FirebaseAdmin.Messaging;

namespace FanHub.NotificationService.Push;

public sealed record PushResult(string Outcome, string? MessageId = null, string? Error = null);
public interface IPushSender
{
    Task<PushResult> SendAsync(string token, Guid notificationId, CancellationToken ct);
}
public sealed class FirebasePushSender(FirebaseMessaging messaging) : IPushSender
{
    public async Task<PushResult> SendAsync(string token, Guid notificationId, CancellationToken ct)
    {
        try
        {
            var id = notificationId.ToString("D");
            var messageId = await messaging.SendAsync(new Message
            {
                // Task 54 accepts existing FCM registration tokens, still supported by HTTP v1.
#pragma warning disable CS0618
                Token = token,
#pragma warning restore CS0618
                // FCM may deliver after logout. Never put personal notification content in a push.
                Notification = new FirebaseAdmin.Messaging.Notification { Title = "FanHub", Body = "Bạn có thông báo mới." },
                Data = new Dictionary<string, string> { ["notification_id"] = id },
                Android = new AndroidConfig { TimeToLive = TimeSpan.FromHours(1), Notification = new AndroidNotification { Tag = id } },
                Apns = new ApnsConfig { Headers = new Dictionary<string, string> { ["apns-collapse-id"] = id, ["apns-expiration"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString() } },
                Webpush = new WebpushConfig { Headers = new Dictionary<string, string> { ["TTL"] = "3600" }, Notification = new WebpushNotification { Tag = id } }
            }, ct);
            return new("Sent", messageId);
        }
        catch (FirebaseMessagingException ex)
        {
            var code = ex.MessagingErrorCode?.ToString() ?? "FirebaseError";
            return ex.MessagingErrorCode switch
            {
                MessagingErrorCode.Unregistered => new("InvalidToken", Error: code),
                MessagingErrorCode.InvalidArgument or MessagingErrorCode.SenderIdMismatch => new("Permanent", Error: code),
                _ => new("Retry", Error: code)
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new("Retry", Error: "Timeout"); }
        catch (HttpRequestException) { return new("Retry", Error: "TransportError"); }
    }
}
