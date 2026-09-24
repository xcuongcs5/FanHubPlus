using FanHub.Shared.Contracts.Events;
using MassTransit;

namespace FanHub.EventService.Messaging;

public sealed class CategoryConsumer(ProjectionHandler handler) : IConsumer<CategoryChangedEvent>
{
    public Task Consume(ConsumeContext<CategoryChangedEvent> context) => handler.CategoryAsync(context.MessageId ?? Guid.Empty, context.Message, context.CancellationToken);
}
public sealed class AttendeeConsumer(ProjectionHandler handler) : IConsumer<BookingAttendeeChangedEvent>
{
    public Task Consume(ConsumeContext<BookingAttendeeChangedEvent> context) => handler.AttendeeAsync(context.MessageId ?? Guid.Empty, context.Message, context.CancellationToken);
}
public sealed class ReviewConsumer(ProjectionHandler handler) : IConsumer<EventReviewDecisionEvent>
{
    public Task Consume(ConsumeContext<EventReviewDecisionEvent> context) => handler.ReviewAsync(context.MessageId ?? Guid.Empty, context.Message, context.CancellationToken);
}
public sealed class UserCreatedConsumer(ProjectionHandler handler) : IConsumer<UserCreatedEvent>
{
    public Task Consume(ConsumeContext<UserCreatedEvent> context) => handler.UserAsync(context.MessageId ?? Guid.Empty, context.Message.UserId,
        context.Message.FullName, context.Message.AvatarUrl, context.Message.CreatedAt, false, context.CancellationToken);
}
public sealed class UserUpdatedConsumer(ProjectionHandler handler) : IConsumer<UserUpdatedEvent>
{
    public Task Consume(ConsumeContext<UserUpdatedEvent> context) => handler.UserAsync(context.MessageId ?? Guid.Empty, context.Message.UserId,
        context.Message.FullName, context.Message.AvatarUrl, context.Message.UpdatedAt, false, context.CancellationToken);
}
public sealed class UserBannedConsumer(ProjectionHandler handler) : IConsumer<UserBannedEvent>
{
    public Task Consume(ConsumeContext<UserBannedEvent> context) => handler.UserAsync(context.MessageId ?? Guid.Empty, context.Message.UserId,
        null, null, context.Message.BannedAt, true, context.CancellationToken);
}
