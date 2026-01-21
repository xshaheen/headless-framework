using Headless.Messaging;

namespace Demo;

public record ShowTimeEvent(DateTime Date);

public sealed class EventConsumer : IConsume<ShowTimeEvent>
{
    public ValueTask Consume(ConsumeContext<ShowTimeEvent> context, CancellationToken cancellationToken)
    {
        Console.WriteLine(context.Message.Date);
        return ValueTask.CompletedTask;
    }
}
