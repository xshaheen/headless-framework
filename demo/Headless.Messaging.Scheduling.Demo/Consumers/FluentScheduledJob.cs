using Headless.Messaging;

namespace Demo.Consumers;

/// Fluent-registered scheduled job (see Program.cs).
public sealed class FluentScheduledJob(IOutboxPublisher publisher, ILogger<FluentScheduledJob> logger)
    : IConsume<ScheduledTrigger>
{
    public async ValueTask Consume(ConsumeContext<ScheduledTrigger> context, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Fluent scheduled job fired at {ScheduledTime} (Attempt: {Attempt})",
            context.Message.ScheduledTime,
            context.Message.Attempt
        );

        await publisher.PublishAsync(
            new PingMessage("fluent-schedule", DateTimeOffset.UtcNow),
            cancellationToken: cancellationToken
        );
    }
}
