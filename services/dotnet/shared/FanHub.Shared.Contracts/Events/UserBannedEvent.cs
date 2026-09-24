namespace FanHub.Shared.Contracts.Events;

public record UserBannedEvent
{
    public Guid UserId { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTime BannedAt { get; init; }
}
