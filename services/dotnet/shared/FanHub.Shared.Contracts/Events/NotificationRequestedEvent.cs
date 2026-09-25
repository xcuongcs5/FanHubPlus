namespace FanHub.Shared.Contracts.Events;

// Trusted service-to-service command, not an HTTP request from an end user.
// Stable MessageId is required for redelivery. Do not put credentials or device tokens here.
public record NotificationRequestedEvent(Guid UserId, string Type, string Title, string Message,
    Dictionary<string, string>? Data = null);
