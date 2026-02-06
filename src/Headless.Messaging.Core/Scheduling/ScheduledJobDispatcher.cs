// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Scheduling;

/// <summary>
/// Dispatches scheduled job executions to keyed <see cref="IConsume{TMessage}"/> handlers
/// resolved by <see cref="ScheduledJob.Name"/>.
/// </summary>
internal sealed class ScheduledJobDispatcher(IServiceScopeFactory scopeFactory) : IScheduledJobDispatcher
{
    /// <inheritdoc />
    public async Task DispatchAsync(
        ScheduledJob job,
        JobExecution execution,
        CancellationToken cancellationToken = default
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope().ConfigureAwait(false);

        var handler = scope.ServiceProvider.GetRequiredKeyedService<IConsume<ScheduledTrigger>>(job.Name);

        var context = new ConsumeContext<ScheduledTrigger>
        {
            MessageId = execution.Id.ToString(),
            Topic = job.Name,
            Timestamp = execution.ScheduledTime,
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Message = new ScheduledTrigger
            {
                JobName = job.Name,
                ScheduledTime = execution.ScheduledTime,
                Attempt = execution.RetryAttempt + 1,
                CronExpression = job.CronExpression,
                ParentJobId = null,
                Payload = job.Payload,
            },
        };

        if (handler is IConsumerLifecycle lifecycle)
        {
            await lifecycle.OnStartingAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await handler.Consume(context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (handler is IConsumerLifecycle lifecycleCleanup)
            {
                try
                {
                    await lifecycleCleanup.OnStoppingAsync(cancellationToken).ConfigureAwait(false);
                }
#pragma warning disable ERP022
                catch
                {
                    // Suppress cleanup exceptions to avoid masking original exceptions
                }
#pragma warning restore ERP022
            }
        }
    }
}
