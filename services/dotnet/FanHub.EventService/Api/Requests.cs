using System.ComponentModel.DataAnnotations;

namespace FanHub.EventService.Api;

public sealed class LocationRequest
{
    [Required, StringLength(200)] public string Name { get; init; } = "";
    [Required, StringLength(500)] public string Address { get; init; } = "";
    [Required, Range(typeof(decimal), "-90", "90")] public decimal? Latitude { get; init; }
    [Required, Range(typeof(decimal), "-180", "180")] public decimal? Longitude { get; init; }
}

public class CreateEventRequest : IValidatableObject
{
    [Required, StringLength(250)] public string Title { get; init; } = "";
    [Required, StringLength(20000)] public string Description { get; init; } = "";
    [StringLength(2048)] public string? BannerUrl { get; init; }
    [Required] public DateTimeOffset? StartTime { get; init; }
    [Required] public DateTimeOffset? EndTime { get; init; }
    [Range(1, 10000000)] public int Capacity { get; init; }
    [Required] public LocationRequest Location { get; init; } = null!;

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Description))
            yield return new("Title and description must not be blank.");
        if (EndTime <= StartTime) yield return new("End time must be after start time.");
        if (StartTime <= DateTimeOffset.UtcNow) yield return new("Start time must be in the future.");
        if (BannerUrl is not null && (!Uri.TryCreate(BannerUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            yield return new("Banner URL must use http or https.");
        // Content is plain text. Rich HTML is not part of this API contract.
        if (Title.Contains('<') || Description.Contains('<')) yield return new("HTML markup is not accepted.");
    }
}

public sealed class UpdateEventRequest : CreateEventRequest
{
    [Required, StringLength(12, MinimumLength = 12)] public string Version { get; init; } = "";
}

public sealed class CategoriesRequest : IValidatableObject
{
    [Required, MinLength(1), MaxLength(20)] public Guid[] CategoryIds { get; init; } = [];
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (CategoryIds is not null && (CategoryIds.Contains(Guid.Empty) || CategoryIds.Distinct().Count() != CategoryIds.Length))
            yield return new("Category IDs must be unique nonempty UUIDs.");
    }
}

public sealed class StaffRequest : IValidatableObject
{
    public Guid UserId { get; init; }
    [Required, RegularExpression("^(CheckIn|Manager)$")] public string Role { get; init; } = "CheckIn";
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (UserId == Guid.Empty) yield return new("User ID is required.");
    }
}

public sealed class PageQuery
{
    [Range(1, 1000000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int Limit { get; init; } = 20;
    [RegularExpression("^(newest|oldest|start_time)$")] public string Sort { get; init; } = "newest";
}

public sealed class ApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
