using Headless.Messaging;

namespace Demo;

public record ShowTimeEvent(DateTime Date);

public sealed record ShowTimeResponse(DateTime Date, string Display);

public sealed class EventConsumer : IConsume<ShowTimeEvent>
{
    public ValueTask ConsumeAsync(ConsumeContext<ShowTimeEvent> context, CancellationToken cancellationToken)
    {
        Console.WriteLine(context.Message.Date);
        context.SetResponse(new ShowTimeResponse(context.Message.Date, context.Message.Date.ToString("O")));
        return ValueTask.CompletedTask;
    }
}

public sealed class ShowTimeResponseConsumer : IConsume<ShowTimeResponse>
{
    public ValueTask ConsumeAsync(ConsumeContext<ShowTimeResponse> context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Callback response: {context.Message.Display}");
        return ValueTask.CompletedTask;
    }
}
