namespace FanHub.Shared.Contracts.Events;

// Full snapshots. SourceVersion is monotonic per source aggregate, not a shared DB version.
public record EventChangedEvent(
    Guid EventId, Guid OrganizerId, string Title, string Description, string? BannerUrl,
    DateTime StartTime, DateTime EndTime, int Capacity, string Status, bool IsFeatured,
    Guid LocationId, string LocationName, string Address, decimal Latitude, decimal Longitude,
    Guid[] CategoryIds, long SourceVersion);

public record EventSubmittedForReviewEvent(
    Guid EventId, Guid ReviewRequestId, string Title, string Description,
    string? BannerUrl, long SourceVersion);

public record EventStaffChangedEvent(
    Guid EventId, Guid UserId, string Role, bool IsActive, long SourceVersion);

// Produced by Community/CMS, Booking and the moderation workers respectively.
public record CategoryChangedEvent(
    Guid CategoryId, Guid? ParentId, string Name, bool IsDeleted, long SourceVersion);

public record BookingAttendeeChangedEvent(
    Guid BookingId, Guid EventId, Guid UserId, string Status,
    DateTime? CheckedInAt, long SourceVersion);

public record EventReviewDecisionEvent(
    Guid EventId, Guid ReviewRequestId, string Source, string Decision,
    Guid? ReviewerId, string? Reason);
