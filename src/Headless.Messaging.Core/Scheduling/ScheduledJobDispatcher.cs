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
        var scope = scopeFactory.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            var handler =
                scope.ServiceProvider.GetKeyedService<IConsume<ScheduledTrigger>>(job.Name)
                ?? _ResolveFromTypeName(scope.ServiceProvider, job.ConsumerTypeName, job.Name);

            var correlationId = execution.Id.ToString();
            var context = new ConsumeContext<ScheduledTrigger>
            {
                MessageId = execution.Id.ToString(),
                Topic = job.Name,
                Timestamp = execution.ScheduledTime,
                CorrelationId = correlationId,
                Headers = new MessageHeader(
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        { Headers.CorrelationId, correlationId },
                        { Headers.CorrelationSequence, "0" },
                    }
                ),
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

            using var correlationScope = MessagingCorrelationScope.Begin(correlationId);

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

    private static IConsume<ScheduledTrigger> _ResolveFromTypeName(
        IServiceProvider sp,
        string? typeName,
        string jobName
    )
    {
        if (typeName is null)
        {
            throw new InvalidOperationException($"No consumer registered for job '{jobName}'.");
        }

        var type =
            Type.GetType(typeName)
            ?? throw new InvalidOperationException($"Consumer type '{typeName}' not found for job '{jobName}'.");

        return (IConsume<ScheduledTrigger>)ActivatorUtilities.CreateInstance(sp, type);
    }
}
