using Headless.Messaging;

namespace Demo.Consumers;

// Emitted by the recurring heartbeat job.
public sealed record HeartbeatMessage(string Source, DateTimeOffset SentAt);

/// Consumer for heartbeat messages produced by the recurring job.
public sealed class HeartbeatConsumer(ILogger<HeartbeatConsumer> logger) : IConsume<HeartbeatMessage>
{
    public ValueTask Consume(ConsumeContext<HeartbeatMessage> context, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Heartbeat received from {Source} at {Timestamp}",
            context.Message.Source,
            context.Message.SentAt
        );
        return ValueTask.CompletedTask;
    }
}
