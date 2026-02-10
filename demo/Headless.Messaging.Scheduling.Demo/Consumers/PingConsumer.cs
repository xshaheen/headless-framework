using Headless.Messaging;

namespace Demo.Consumers;

// Basic message used to demonstrate simple publish/consume flow.
public sealed record PingMessage(string Text, DateTimeOffset SentAt);

/// Simple consumer for PingMessage to show basic publish/consume flow.
public sealed class PingConsumer(ILogger<PingConsumer> logger) : IConsume<PingMessage>
{
    public ValueTask Consume(ConsumeContext<PingMessage> context, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Ping received: {Text} at {Timestamp} (MessageId: {MessageId})",
            context.Message.Text,
            context.Message.SentAt,
            context.MessageId
        );
        return ValueTask.CompletedTask;
    }
}
