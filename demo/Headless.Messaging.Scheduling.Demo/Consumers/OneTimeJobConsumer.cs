using Headless.Messaging;

namespace Demo.Consumers;

/// One-time job consumer for ScheduleOnceAsync API.
public sealed class OneTimeJobConsumer(IOutboxPublisher publisher, ILogger<OneTimeJobConsumer> logger)
    : IConsume<ScheduledTrigger>
{
    // Use shared serializer options to avoid repeated allocations.
    private static readonly JsonSerializerOptions _JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask Consume(ConsumeContext<ScheduledTrigger> context, CancellationToken cancellationToken)
    {
        var trigger = context.Message;
        var payload = _TryReadPayload(trigger.Payload);

        logger.LogInformation(
            "One-time job fired at {ScheduledTime} with payload {Payload}",
            trigger.ScheduledTime,
            trigger.Payload ?? "<none>"
        );

        var workItem = new WorkItemMessage(
            $"one-time-{Guid.NewGuid():N}",
            payload?.ShouldFail ?? false,
            payload?.FailuresBeforeSuccess ?? 1
        );

        await publisher.PublishAsync(workItem, cancellationToken: cancellationToken);
    }

    private static ScheduleOncePayload? _TryReadPayload(string? payload)
    {
        // Payload is optional JSON from the scheduling API.
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ScheduleOncePayload>(payload, _JsonOptions);
        }
        catch (JsonException)
        {
            // Ignore invalid JSON to keep the demo resilient.
            return null;
        }
    }
}
