using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FanHub.NotificationService.Data;
using FanHub.NotificationService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace FanHub.NotificationService.Api;

public sealed class PageQuery
{
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 100), FromQuery(Name = "page_size")] public int PageSize { get; set; } = 20;
    [FromQuery(Name = "unread_only")] public bool UnreadOnly { get; set; }
}
public sealed record DeviceRequest([Required, RegularExpression("^FCM$")] string Provider,
    Guid DeviceId, [Required, StringLength(2048, MinimumLength = 20)] string Token, bool IsActive = true);
public sealed class ApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

[ApiController, Authorize, Route("api/v1/notifications"), EnableRateLimiting("user")]
[RequestSizeLimit(16384)]
public sealed class NotificationsController(NotificationDbContext db, NotificationStore store) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<object> List([FromQuery] PageQuery query, CancellationToken ct)
    {
        var own = db.Notifications.AsNoTracking().Where(x => x.UserId == UserId);
        var filtered = query.UnreadOnly ? own.Where(x => x.ReadAt == null) : own;
        return new { total = await filtered.CountAsync(ct), unread_count = await own.CountAsync(x => x.ReadAt == null, ct), query.Page, query.PageSize,
            items = await filtered.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.NotificationId)
                .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
                .Select(x => new { x.NotificationId, x.Type, x.Title, x.Message, x.DataJson, x.ReadAt, x.CreatedAt }).ToListAsync(ct) };
    }
    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        await db.Notifications.Where(x => x.NotificationId == id && x.UserId == UserId && x.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, DateTime.UtcNow), ct);
        if (!await db.Notifications.AnyAsync(x => x.NotificationId == id && x.UserId == UserId, ct)) return NotFound();
        return NoContent();
    }
    [HttpPut("read-all")]
    public async Task<object> ReadAll(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow;
        var count = await db.Notifications.Where(x => x.UserId == UserId && x.ReadAt == null && x.CreatedAt <= cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, cutoff), ct);
        return new { updated_count = count, read_at = cutoff };
    }
    [HttpPost("device-token")]
    public async Task<IActionResult> Register(DeviceRequest input, CancellationToken ct)
    {
        var token = await store.RegisterAsync(UserId, input, ct);
        return Ok(new { token.DeviceTokenId, token.DeviceId, token.Provider, token.IsActive, token.LastSeenAt });
    }
}
