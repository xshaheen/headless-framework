// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Scheduling;

/// <summary>
/// Dispatches scheduled job executions to keyed <see cref="IConsume{TMessage}"/> handlers
/// resolved by <see cref="ScheduledJob.Name"/>.
/// </summary>
internal sealed class ScheduledJobDispatcher(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
    : IScheduledJobDispatcher
{
    private static readonly DiagnosticSource _DiagnosticListener = new DiagnosticListener(
        MessageDiagnosticListenerNames.DiagnosticListenerName
    );

    private static readonly ConcurrentDictionary<
        string,
        Func<IServiceProvider, IConsume<ScheduledTrigger>>
    > _FactoryCache = new(StringComparer.Ordinal);

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
            var factory = _FactoryCache.GetOrAdd(
                job.Name,
                static (name, state) =>
                {
                    var j = state;

                    if (j.ConsumerTypeName is not null)
                    {
                        var type =
                            Type.GetType(j.ConsumerTypeName)
                            ?? throw new InvalidOperationException(
                                $"Consumer type '{j.ConsumerTypeName}' not found for job '{name}'."
                            );

                        return serviceProvider =>
                            (IConsume<ScheduledTrigger>)ActivatorUtilities.CreateInstance(serviceProvider, type);
                    }

                    return serviceProvider => serviceProvider.GetRequiredKeyedService<IConsume<ScheduledTrigger>>(name);
                },
                job
            );

            var handler = factory(scope.ServiceProvider);

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
                    Payload = job.Payload,
                },
            };

            using var correlationScope = MessagingCorrelationScope.Begin(correlationId);

            if (handler is IConsumerLifecycle lifecycle)
            {
                await lifecycle.OnStartingAsync(cancellationToken).ConfigureAwait(false);
            }

            long? tracingTimestamp = null;
            try
            {
                tracingTimestamp = _TracingBefore(job, execution);
                await handler.Consume(context, cancellationToken).ConfigureAwait(false);
                _TracingAfter(tracingTimestamp, job, execution);
            }
            catch (Exception ex)
            {
                _TracingError(tracingTimestamp, job, execution, ex);
                throw;
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

    private long? _TracingBefore(ScheduledJob job, JobExecution execution)
    {
        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforeScheduledJobDispatch))
        {
            var eventData = new ScheduledJobEventData
            {
                JobName = job.Name,
                ExecutionId = execution.Id,
                Attempt = execution.RetryAttempt + 1,
                ScheduledTime = execution.ScheduledTime,
                OperationTimestamp = timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforeScheduledJobDispatch, eventData);
            return eventData.OperationTimestamp;
        }

        return null;
    }

    private void _TracingAfter(long? tracingTimestamp, ScheduledJob job, JobExecution execution)
    {
        if (
            tracingTimestamp != null
            && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterScheduledJobDispatch)
        )
        {
            var now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            var eventData = new ScheduledJobEventData
            {
                JobName = job.Name,
                ExecutionId = execution.Id,
                Attempt = execution.RetryAttempt + 1,
                ScheduledTime = execution.ScheduledTime,
                OperationTimestamp = now,
                ElapsedTimeMs = now - tracingTimestamp.Value,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterScheduledJobDispatch, eventData);
        }
    }

    private void _TracingError(long? tracingTimestamp, ScheduledJob job, JobExecution execution, Exception ex)
    {
        if (
            tracingTimestamp != null
            && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorScheduledJobDispatch)
        )
        {
            var now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            var eventData = new ScheduledJobEventData
            {
                JobName = job.Name,
                ExecutionId = execution.Id,
                Attempt = execution.RetryAttempt + 1,
                ScheduledTime = execution.ScheduledTime,
                OperationTimestamp = now,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                Exception = ex,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorScheduledJobDispatch, eventData);
        }
    }
}
