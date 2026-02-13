using Headless.Messaging;

namespace Demo.Consumers;

/// Attribute-based recurring job (fires every 10 seconds).
[Recurring("*/10 * * * * *", Name = "demo-heartbeat", RetryIntervals = [1, 5], TimeoutSeconds = 5)]
public sealed class HeartbeatJob(IOutboxPublisher publisher, ILogger<HeartbeatJob> logger) : IConsume<ScheduledTrigger>
{
    public async ValueTask Consume(ConsumeContext<ScheduledTrigger> context, CancellationToken cancellationToken)
    {
        var trigger = context.Message;
        var message = new HeartbeatMessage(trigger.JobName, DateTimeOffset.UtcNow);

        logger.LogInformation(
            "Heartbeat job fired at {ScheduledTime} (Attempt: {Attempt})",
            trigger.ScheduledTime,
            trigger.Attempt
        );

        await publisher.PublishAsync(message, cancellationToken: cancellationToken);
    }
}
