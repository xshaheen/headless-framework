using Framework.Messages;

namespace Demo;

public record SampleMessage(string Content);

public sealed class SampleSubscriber : IConsume<SampleMessage>
{
    public ValueTask Consume(ConsumeContext<SampleMessage> context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Message {context.Message.Content} received");
        return ValueTask.CompletedTask;
    }
}
