using System.Security.Claims;
using FanHub.EventService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FanHub.EventService.Api;

[ApiController]
[Authorize]
[Route("api/v1/events")]
public sealed class EventsController(Services.EventService service) : ControllerBase
{
    private Actor Actor => new(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), User.IsInRole("Admin"));

    [HttpGet]
    public Task<object> List([FromQuery] PageQuery query, CancellationToken ct) => service.ListAsync(query, Actor, false, false, ct);

    [HttpGet("{id:guid}")]
    public Task<object> Details(Guid id, CancellationToken ct) => service.DetailsAsync(id, Actor, ct);

    [HttpGet("organizer/me"), Authorize(Roles = "EventOwner,Admin")]
    public Task<object> Mine([FromQuery] PageQuery query, CancellationToken ct) => service.ListAsync(query, Actor, true, false, ct);

    [HttpGet("featured")]
    public Task<object> Featured([FromQuery] PageQuery query, CancellationToken ct) => service.ListAsync(query, Actor, false, true, ct);

    [HttpPost, Authorize(Roles = "EventOwner,Admin")]
    public async Task<IActionResult> Create(CreateEventRequest request, CancellationToken ct)
    {
        var id = await service.CreateAsync(request, Actor, ct);
        return CreatedAtAction(nameof(Details), new { id }, new { Id = id, Message = "Event created as Draft." });
    }

    [HttpPut("{id:guid}"), Authorize(Roles = "EventOwner,Admin")]
    public async Task<IActionResult> Update(Guid id, UpdateEventRequest request, CancellationToken ct)
    {
        await service.UpdateAsync(id, request, Actor, ct);
        return Ok(new { Message = "Event updated." });
    }

    [HttpPost("{id:guid}/publish"), Authorize(Roles = "EventOwner,Admin")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) => Accepted(await service.PublishAsync(id, Actor, ct));

    [HttpDelete("{id:guid}"), Authorize(Roles = "EventOwner,Admin")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await service.CancelAsync(id, Actor, ct);
        return Ok(new { Message = "Event cancelled." });
    }

    [HttpGet("{id:guid}/attendees"), Authorize(Roles = "EventOwner,Admin")]
    public Task<object> Attendees(Guid id, [FromQuery] PageQuery query, CancellationToken ct) => service.AttendeesAsync(id, query, Actor, ct);

    [HttpPost("{id:guid}/categories"), Authorize(Roles = "EventOwner,Admin")]
    public async Task<IActionResult> Categories(Guid id, CategoriesRequest request, CancellationToken ct)
    {
        await service.AttachCategoriesAsync(id, request, Actor, ct);
        return Ok(new { Message = "Categories assigned." });
    }

    [HttpPost("{id:guid}/staffs"), Authorize(Roles = "EventOwner,Admin")]
    public async Task<IActionResult> Staffs(Guid id, StaffRequest request, CancellationToken ct)
    {
        await service.AddStaffAsync(id, request, Actor, ct);
        return Ok(new { Message = "Staff assigned." });
    }
}

[ApiController, Authorize, Route("api/v1/categories")]
public sealed class CategoriesController(Services.EventService service) : ControllerBase
{
    [HttpGet]
    public Task<object> List([FromQuery] PageQuery query, CancellationToken ct) => service.CategoriesAsync(query, ct);
}
