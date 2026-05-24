using Headless.Messaging;

namespace Demo;

public record SampleMessage(string Content);

public sealed class SampleSubscriber : IConsume<SampleMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<SampleMessage> context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Message {context.Message.Content} received");
        return ValueTask.CompletedTask;
    }
}
