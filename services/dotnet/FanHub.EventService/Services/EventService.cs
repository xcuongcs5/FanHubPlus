using System.Buffers.Binary;
using System.Text.Json;
using FanHub.EventService.Api;
using FanHub.EventService.Data;
using FanHub.Shared.Contracts.Events;
using Microsoft.EntityFrameworkCore;

namespace FanHub.EventService.Services;

public sealed record Actor(Guid UserId, bool IsAdmin);

public sealed class EventService(EventDbContext db)
{
    public static bool CanManage(EventEntity item, Actor actor) => actor.IsAdmin || item.OrganizerId == actor.UserId;
    public static long SourceVersion(EventEntity item) => BinaryPrimitives.ReadInt64BigEndian(item.Version);

    public async Task<object> ListAsync(PageQuery page, Actor actor, bool mine, bool featured, CancellationToken ct)
    {
        var query = db.Events.AsNoTracking();
        query = mine ? query.Where(x => x.OrganizerId == actor.UserId) : query.Where(x => x.Status == "Published");
        if (featured) query = query.Where(x => x.IsFeatured && x.EndTime > DateTime.UtcNow);
        var total = await query.CountAsync(ct);
        var sorted = page.Sort switch
        {
            "oldest" => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.EventId),
            "start_time" => query.OrderBy(x => x.StartTime).ThenBy(x => x.EventId),
            _ => query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.EventId)
        };
        var data = await (from x in sorted.Skip((page.Page - 1) * page.Limit).Take(page.Limit) join loc in db.Locations on x.LocationId equals loc.LocationId select new { Id = x.EventId, x.OrganizerId, x.Title, x.BannerUrl, x.StartTime, x.EndTime, x.Capacity, x.Status, x.IsFeatured, Location = new { Id = loc.LocationId, Name = loc.Name, Address = loc.Address, Latitude = loc.Latitude, Longitude = loc.Longitude }, x.CreatedAt, Version = Convert.ToBase64String(x.Version) }).ToListAsync(ct);
        return new { Data = data, Meta = new { total, page.Page, page.Limit } };
    }

    public async Task<object> DetailsAsync(Guid id, Actor actor, CancellationToken ct)
    {
        var item = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == id, ct) ?? throw NotFound();
        if (!CanManage(item, actor) && !(item.PublishedAt is not null && item.Status is "Published" or "Completed" or "Cancelled"))
            throw NotFound();
        var location = await db.Locations.AsNoTracking().SingleAsync(x => x.LocationId == item.LocationId, ct);
        var categories = await (from link in db.EventCategories
                                join category in db.Categories on link.CategoryId equals category.CategoryId
                                where link.EventId == id && !category.IsDeleted
                                select new { Id = category.CategoryId, category.Name }).ToListAsync(ct);
        return new { Id = item.EventId, item.OrganizerId, item.Title, item.Description, item.BannerUrl,
            item.StartTime, item.EndTime, item.Capacity, item.Status, item.IsFeatured, item.CreatedAt,
            item.UpdatedAt, item.PublishedAt, item.CancelledAt, item.CancellationReason,
            Version = Convert.ToBase64String(item.Version),
            Location = new { Id = location.LocationId, location.Name, location.Address, location.Latitude, location.Longitude },
            Categories = categories };
    }

    public async Task<Guid> CreateAsync(CreateEventRequest request, Actor actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var item = new EventEntity { OrganizerId = actor.UserId };
        Apply(item, request);
        db.Events.Add(item);
        await SaveSnapshotAsync(item, ct);
        await tx.CommitAsync(ct);
        return item.EventId;
    }

    public async Task UpdateAsync(Guid id, UpdateEventRequest request, Actor actor, CancellationToken ct)
    {
        byte[] version;
        try { version = Convert.FromBase64String(request.Version); }
        catch (FormatException) { throw new ApiException(400, "invalid_version", "Version must be a base64 rowversion."); }
        if (version.Length != 8) throw new ApiException(400, "invalid_version", "Version must be an 8-byte rowversion.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var item = await ManagedAsync(id, actor, ct);
        Editable(item);
        if (!version.SequenceEqual(item.Version)) throw new ApiException(409, "stale_version", "Event changed. Reload before editing.");
        Apply(item, request);
        // Any rejected submission becomes a new draft. Old moderation results cannot approve it.
        item.Status = "Draft";
        item.ReviewRequestId = null;
        await SaveSnapshotAsync(item, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<object> PublishAsync(Guid id, Actor actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var item = await ManagedAsync(id, actor, ct);
        if (item.Status == "PendingReview") return new { Id = id, item.Status, item.ReviewRequestId };
        Editable(item);
        if (item.StartTime <= DateTime.UtcNow) throw new ApiException(409, "event_started", "Cannot submit an event that has started.");
        var activeCategories = await (from link in db.EventCategories join category in db.Categories
            on link.CategoryId equals category.CategoryId where link.EventId == id && !category.IsDeleted select link).AnyAsync(ct);
        if (!activeCategories) throw new ApiException(409, "category_required", "Assign an active category before submitting for review.");
        item.Status = "PendingReview";
        item.ReviewRequestId = Guid.NewGuid();
        await SaveSnapshotAsync(item, ct);
        AddOutbox(item, new EventSubmittedForReviewEvent(id, item.ReviewRequestId.Value,
            item.Title, item.Description, item.BannerUrl, SourceVersion(item)), item.ReviewRequestId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new { Id = id, item.Status, item.ReviewRequestId };
    }

    public async Task CancelAsync(Guid id, Actor actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var item = await ManagedAsync(id, actor, ct);
        if (item.Status == "Cancelled") return;
        if (item.Status == "Completed" || item.EndTime <= DateTime.UtcNow)
            throw new ApiException(409, "event_completed", "Completed events cannot be cancelled.");
        item.Status = "Cancelled";
        item.CancelledAt = DateTime.UtcNow;
        item.CancellationReason = "Cancelled by organizer or administrator";
        item.ReviewRequestId = null;
        await SaveSnapshotAsync(item, ct);
        await tx.CommitAsync(ct);
    }

    public async Task AttachCategoriesAsync(Guid id, CategoriesRequest request, Actor actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var item = await ManagedAsync(id, actor, ct);
        Editable(item);
        if (await db.Categories.CountAsync(x => request.CategoryIds.Contains(x.CategoryId) && !x.IsDeleted, ct) != request.CategoryIds.Length)
            throw new ApiException(409, "category_unavailable", "Category is missing, deleted or has not synchronized yet.");
        var existing = await db.EventCategories.Where(x => x.EventId == id).Select(x => x.CategoryId).ToListAsync(ct);
        var added = request.CategoryIds.Except(existing).ToArray();
        if (existing.Count + added.Length > 20) throw new ApiException(400, "too_many_categories", "An event supports at most 20 categories.");
        if (added.Length == 0) return;
        db.EventCategories.AddRange(added.Select(category => new EventCategory { EventId = id, CategoryId = category }));
        await SaveSnapshotAsync(item, ct);
        await tx.CommitAsync(ct);
    }

    public async Task AddStaffAsync(Guid id, StaffRequest request, Actor actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var item = await ManagedAsync(id, actor, ct);
        if (item.Status is "Cancelled" or "Completed") throw new ApiException(409, "event_closed", "Event is closed.");
        if (!await db.Users.AnyAsync(x => x.UserId == request.UserId && x.Status == "Active", ct))
            throw new ApiException(409, "user_unavailable", "Staff user is inactive or has not synchronized yet.");
        var staff = await db.Staffs.FindAsync([id, request.UserId], ct);
        if (staff is not null && staff.IsActive && staff.Role == request.Role) return;
        if (staff is null)
        {
            staff = new EventStaff { EventId = id, UserId = request.UserId };
            db.Staffs.Add(staff);
        }
        staff.Role = request.Role;
        staff.IsActive = true;
        await SaveSnapshotAsync(item, ct);
        AddOutbox(item, new EventStaffChangedEvent(id, request.UserId, staff.Role, true, SourceVersion(item)));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<object> AttendeesAsync(Guid id, PageQuery page, Actor actor, CancellationToken ct)
    {
        var item = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == id, ct) ?? throw NotFound();
        if (!CanManage(item, actor)) throw new ApiException(403, "forbidden", "Only the organizer or administrator may view attendees.");
        // Each entry represents an issued ticket. A user holding several tickets appears several times.
        var query = from attendee in db.Attendees
                    join user in db.Users on attendee.UserId equals user.UserId into users
                    from user in users.DefaultIfEmpty()
                    where attendee.EventId == id && (attendee.Status == "Paid" || attendee.Status == "MintPending" || attendee.Status == "Active" || attendee.Status == "CheckedIn")
                    select new { attendee.BookingId, attendee.UserId, FullName = user == null ? null : user.FullName,
                        attendee.Status, attendee.CheckedInAt };
        var total = await query.CountAsync(ct);
        var data = await query.OrderBy(x => x.BookingId).Skip((page.Page - 1) * page.Limit).Take(page.Limit).ToListAsync(ct);
        return new { Data = data, Meta = new { total, page.Page, page.Limit } };
    }

    public async Task<object> CategoriesAsync(PageQuery page, CancellationToken ct)
    {
        var query = db.Categories.AsNoTracking().Where(x => !x.IsDeleted);
        var total = await query.CountAsync(ct);
        var data = await query.OrderBy(x => x.Name).ThenBy(x => x.CategoryId).Skip((page.Page - 1) * page.Limit)
            .Take(page.Limit).Select(x => new { Id = x.CategoryId, x.Name, x.ParentId }).ToListAsync(ct);
        return new { Data = data, Meta = new { total, page.Page, page.Limit } };
    }

    public async Task<EventEntity?> LockAsync(Guid id, CancellationToken ct) => await db.Events
        .FromSqlInterpolated($"SELECT * FROM dbo.EVENT WITH (UPDLOCK, ROWLOCK) WHERE event_id={id}")
        .SingleOrDefaultAsync(ct);

    private async Task<EventEntity> ManagedAsync(Guid id, Actor actor, CancellationToken ct)
    {
        var item = await LockAsync(id, ct) ?? throw NotFound();
        if (!CanManage(item, actor)) throw new ApiException(403, "forbidden", "Only the organizer or administrator may modify this event.");
        return item;
    }

    private static ApiException NotFound() => new(404, "event_not_found", "Event not found.");
    private static void Editable(EventEntity item)
    {
        if (item.Status is not ("Draft" or "Rejected")) throw new ApiException(409, "event_not_editable", "Only Draft or Rejected events can be edited or submitted.");
    }

    private void Apply(EventEntity item, CreateEventRequest request)
    {
        var location = new LocationEntity { Name = request.Location.Name.Trim(), Address = request.Location.Address.Trim(),
            Latitude = request.Location.Latitude!.Value, Longitude = request.Location.Longitude!.Value };
        db.Locations.Add(location);
        item.LocationId = location.LocationId;
        item.Title = request.Title.Trim();
        item.Description = request.Description.Trim();
        item.BannerUrl = request.BannerUrl;
        item.StartTime = request.StartTime!.Value.UtcDateTime;
        item.EndTime = request.EndTime!.Value.UtcDateTime;
        item.Capacity = request.Capacity;
    }

    public async Task SaveSnapshotAsync(EventEntity item, CancellationToken ct)
    {
        item.UpdatedAt = DateTime.UtcNow;
        if (db.Entry(item).State != EntityState.Added) db.Entry(item).Property(x => x.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(ct); // Get the new SQL rowversion within the caller's transaction.
        var location = await db.Locations.SingleAsync(x => x.LocationId == item.LocationId, ct);
        var categories = await db.EventCategories.Where(x => x.EventId == item.EventId).Select(x => x.CategoryId).ToArrayAsync(ct);
        AddOutbox(item, new EventChangedEvent(item.EventId, item.OrganizerId, item.Title, item.Description, item.BannerUrl,
            item.StartTime, item.EndTime, item.Capacity, item.Status, item.IsFeatured, location.LocationId,
            location.Name, location.Address, location.Latitude, location.Longitude, categories, SourceVersion(item)));
        await db.SaveChangesAsync(ct);
    }

    public void AddOutbox<T>(EventEntity item, T message, Guid? correlationId = null) where T : class => db.Outbox.Add(new OutboxMessage
    {
        EventType = typeof(T).Name, AggregateId = item.EventId, AggregateVersion = SourceVersion(item),
        CorrelationId = correlationId ?? item.EventId, Payload = JsonSerializer.Serialize(message)
    });
}
