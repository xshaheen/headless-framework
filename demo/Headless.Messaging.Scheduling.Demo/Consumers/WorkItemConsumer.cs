using System.Collections.Concurrent;
using Headless.Messaging;

namespace Demo.Consumers;

// Work item payload with optional failure simulation for retries.
public sealed record WorkItemMessage(string WorkId, bool ShouldFail, int FailuresBeforeSuccess);

/// Work item consumer that can simulate failures to exercise retry flow.
public sealed class WorkItemConsumer(ILogger<WorkItemConsumer> logger) : IConsume<WorkItemMessage>
{
    // Track attempts per WorkId in-memory for demo behavior.
    private static readonly ConcurrentDictionary<string, int> _Attempts = new(StringComparer.Ordinal);

    public ValueTask Consume(ConsumeContext<WorkItemMessage> context, CancellationToken cancellationToken)
    {
        var message = context.Message;
        var attempt = _Attempts.AddOrUpdate(message.WorkId, 1, (_, current) => current + 1);

        if (message.ShouldFail && attempt <= Math.Max(1, message.FailuresBeforeSuccess))
        {
            logger.LogWarning("Work item {WorkId} failing on attempt {Attempt}", message.WorkId, attempt);
            throw new InvalidOperationException($"Simulated failure for {message.WorkId} on attempt {attempt}");
        }

        logger.LogInformation("Work item {WorkId} processed on attempt {Attempt}", message.WorkId, attempt);

        return ValueTask.CompletedTask;
    }
}
