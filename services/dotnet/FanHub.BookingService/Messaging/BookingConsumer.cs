using MassTransit;

namespace FanHub.BookingService.Messaging;

public sealed class BookingConsumer<T>(MessageHandler handler) : IConsumer<T> where T : class
{
    public Task Consume(ConsumeContext<T> context) => handler.ApplyAsync(context.MessageId ?? Guid.Empty, context.Message, context.CancellationToken);
}
