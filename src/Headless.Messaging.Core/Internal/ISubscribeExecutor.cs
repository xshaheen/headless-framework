// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Headless.Checks;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

/// <summary>
/// Consumer executor
/// </summary>
internal interface ISubscribeExecutor
{
    /// <summary>
    /// Executes a single consume attempt with retries.
    /// </summary>
    /// <param name="message">The message to execute.</param>
    /// <param name="dispatchServices">
    /// The live per-message DI scope's <see cref="IServiceProvider"/>. The caller (Dispatcher) creates
    /// this scope and disposes it after the call returns. Surfaces to <c>FailedInfo.ServiceProvider</c>
    /// when the retry budget exhausts, so the user's exhausted callback resolves scoped services from the
    /// SAME scope the consume attempt ran under.
    /// </param>
    /// <param name="descriptor">Optional consumer descriptor; resolved from the topic when omitted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<OperateResult> ExecuteAsync(
        MediumMessage message,
        IServiceProvider dispatchServices,
        ConsumerExecutorDescriptor? descriptor = null,
        CancellationToken cancellationToken = default
    );
}

internal sealed class SubscribeExecutor(
    IServiceProvider provider,
    IDataStorage dataStorage,
    ISubscribeInvoker invoker,
    TimeProvider timeProvider,
    ILogger<SubscribeExecutor> logger,
    IOptions<MessagingOptions> options,
    ICircuitBreakerStateManager? circuitBreakerStateManager = null
) : ISubscribeExecutor
{
    // Diagnostics listener
    private static readonly DiagnosticListener _DiagnosticListener = new(
        MessageDiagnosticListenerNames.DiagnosticListenerName
    );

    private readonly string? _hostName = Helper.GetInstanceHostname();
    private readonly MessagingOptions _options = options.Value;
    private readonly RetryPolicyOptions _retryPolicy = options.Value.RetryPolicy;

    public async Task<OperateResult> ExecuteAsync(
        MediumMessage message,
        IServiceProvider dispatchServices,
        ConsumerExecutorDescriptor? descriptor = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(dispatchServices);

        if (descriptor == null)
        {
            var selector = provider.GetRequiredService<MethodMatcherCache>();
            if (!selector.TryGetTopicExecutor(message.Origin.GetName(), message.Origin.GetGroup()!, out descriptor))
            {
                var safeName = LogSanitizer.Sanitize(message.Origin.GetName());
                var safeGroup = LogSanitizer.Sanitize(message.Origin.GetGroup());

                logger.SubscriberNotFound(safeName, safeGroup);

                var exception = new SubscriberNotFoundException(
                    $"Message (Name:{safeName},Group:{safeGroup}) can not be found subscriber."
                        + $"{Environment.NewLine} Ensure the subscriber method is decorated with [Subscribe] and the consumer group matches."
                );

                _TracingError(
                    timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                    message.Origin,
                    method: null,
                    exception
                );

                await _SetFailedState(message, exception, dispatchServices, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return OperateResult.Failed(exception);
            }
        }

        //record instance id
        message.Origin.Headers[Headers.ExecutionInstanceId] = _hostName;

        return await InlineRetryLoop
            .ExecuteAsync(
                (inlineRetries, ct) =>
                    _ExecuteWithoutRetryAsync(message, descriptor, dispatchServices, inlineRetries, ct),
                _retryPolicy,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task<(RetryDecision Decision, OperateResult Result)> _ExecuteWithoutRetryAsync(
        MediumMessage message,
        ConsumerExecutorDescriptor descriptor,
        IServiceProvider dispatchServices,
        int inlineRetries,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(message);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            logger.ConsumerExecuting(descriptor.ImplTypeInfo.Name, descriptor.MethodInfo.Name, descriptor.GroupName);

            var sp = Stopwatch.StartNew();

            await _InvokeConsumerMethodAsync(message, descriptor, cancellationToken).ConfigureAwait(false);

            sp.Stop();

            await _SetSuccessfulState(message, cancellationToken).ConfigureAwait(false);

            MessageEventCounterSource.Log.WriteInvokeTimeMetrics(sp.Elapsed.TotalMilliseconds);
            if (logger.IsEnabled(LogLevel.Information))
            {
                var executionInstanceId = message.Origin.GetExecutionInstanceId();
                logger.ConsumerExecuted(
                    descriptor.ImplTypeInfo.Name,
                    descriptor.MethodInfo.Name,
                    descriptor.GroupName,
                    sp.Elapsed.TotalMilliseconds,
                    executionInstanceId
                );
            }

            return (RetryDecision.Stop, OperateResult.Success);
        }
        catch (Exception ex)
        {
            logger.ConsumerExecuteFailed(
                ex,
                LogSanitizer.Sanitize(message.Origin.GetName()),
                message.StorageId,
                message.Origin.GetExecutionInstanceId()
            );

            return (
                await _SetFailedState(message, ex, dispatchServices, inlineRetries, cancellationToken)
                    .ConfigureAwait(false),
                OperateResult.Failed(ex)
            );
        }
    }

    private async ValueTask _SetSuccessfulState(MediumMessage message, CancellationToken cancellationToken)
    {
        message.ExpiresAt = timeProvider.GetUtcNow().UtcDateTime.AddSeconds(_options.SucceedMessageExpiredAfter);

        await dataStorage
            .ChangeReceiveStateAsync(message, StatusName.Succeeded, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (circuitBreakerStateManager is not null)
        {
            await circuitBreakerStateManager.ReportSuccessAsync(message.Origin.GetGroup()!).ConfigureAwait(false);
        }
    }

    private async Task<RetryDecision> _SetFailedState(
        MediumMessage message,
        Exception ex,
        IServiceProvider dispatchServices,
        int inlineRetries = 0,
        CancellationToken cancellationToken = default
    )
    {
        // Host shutdown: an OCE bound to the dispatch cancellation token (linked to host stopping)
        // means the dispatch was aborted, not that the message failed. Returning without writing
        // state preserves the row's existing NextRetryAt/Status, and the persisted retry processor
        // will pick the row up on restart.
        var isCancellation = RetryHelper.IsCancellation(ex, cancellationToken);

        if (isCancellation)
        {
            logger.StoredMessageExecutionCanceled(message.StorageId);
            return RetryDecision.Stop;
        }

        var decision = _UpdateMessageForRetry(message, ex, inlineRetries);

        message.Origin.AddOrUpdateException(ex);
        message.ExceptionInfo = ex.ExpandMessage();
        message.ExpiresAt = message.Added.AddSeconds(_options.FailedMessageExpiredAfter);

        // Inline-retry budget still available: persist as Scheduled/NULL so a crash mid-delay
        // leaves the row picked up by the polling query on restart (Failed/NULL is filtered out).
        // Only transition to Failed on terminal decisions (Stop, Exhausted) or when persisting
        // for the persisted-retry processor (Continue with inline budget exhausted, NextRetryAt set).
        var state = RetryHelper.ResolveNextState(decision, inlineRetries, _retryPolicy, timeProvider);

        // Persist transition: inline budget consumed AND decision Continue means the call site
        // owns the Retries++ . The helper is pure with respect to MediumMessage; this is the only
        // place persisted-pickup count advances.
        if (decision.Outcome == RetryDecision.Kind.Continue && !state.IsInlineRetryInFlight)
        {
            message.Retries++;
        }

        var affected = await dataStorage
            .ChangeReceiveStateAsync(
                message,
                state.NextStatus,
                nextRetryAt: state.NextRetryAt,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        if (affected && decision.Outcome == RetryDecision.Kind.Exhausted)
        {
            await _InvokeOnExhausted(message, ex, dispatchServices, cancellationToken).ConfigureAwait(false);
        }
        else if (!affected)
        {
            logger.SkippingOnExhaustedAlreadyTerminal(message.StorageId);
        }

        // Report the original (inner) exception to the circuit breaker so transient-classification
        // predicates see the real exception type, not the SubscriberExecutionFailedException wrapper.
        if (circuitBreakerStateManager is not null && !isCancellation)
        {
            var reportedException = ex is SubscriberExecutionFailedException { InnerException: { } inner } ? inner : ex;
            await circuitBreakerStateManager
                .ReportFailureAsync(message.Origin.GetGroup()!, reportedException)
                .ConfigureAwait(false);
        }

        return decision;
    }

    private RetryDecision _UpdateMessageForRetry(MediumMessage message, Exception ex, int inlineRetries)
    {
        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            ex,
            _retryPolicy,
            inlineRetries,
            isCancellation: false
        );
        switch (decision.Outcome)
        {
            case RetryDecision.Kind.Stop:
                logger.StoredMessageNonRetryableFailure(message.StorageId, ex.GetType().Name);
                break;
            case RetryDecision.Kind.Exhausted:
                logger.ConsumerStoredMessageAfterThreshold(message.StorageId, _retryPolicy.MaxPersistedRetries);
                break;
            case RetryDecision.Kind.Continue:
                logger.ConsumerExecutionRetrying(message.StorageId, message.Retries);
                break;
        }

        return decision;
    }

    private async Task _InvokeOnExhausted(
        MediumMessage message,
        Exception ex,
        IServiceProvider dispatchServices,
        CancellationToken cancellationToken
    )
    {
        var callback = _retryPolicy.OnExhausted;
        if (callback is null)
        {
            return;
        }

        try
        {
            // Use the live dispatch scope so scoped services resolved here are the same
            // instances seen during ExecuteAsync. The caller (Dispatcher) is responsible for
            // creating and disposing that scope.
            await callback(
                    new FailedInfo
                    {
                        ServiceProvider = dispatchServices,
                        MessageType = MessageType.Subscribe,
                        Message = message.Origin,
                        Exception = ex,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception callbackEx)
        {
            logger.ExecutedThresholdCallbackFailed(callbackEx, LogSanitizer.Sanitize(callbackEx.Message));
        }
    }

    private async Task _InvokeConsumerMethodAsync(
        MediumMessage message,
        ConsumerExecutorDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        var consumerContext = new ConsumerContext(descriptor, message);
        var tracingTimestamp = _TracingBefore(message.Origin, descriptor.MethodInfo);
        try
        {
            var ret = await invoker.InvokeAsync(consumerContext, cancellationToken).ConfigureAwait(false);

            _TracingAfter(tracingTimestamp, message.Origin, descriptor.MethodInfo);

            if (!string.IsNullOrEmpty(ret.CallbackName))
            {
                ret.CallbackHeader ??= new Dictionary<string, string?>(StringComparer.Ordinal);
                ret.CallbackHeader[Headers.CorrelationId] = message.Origin.GetId();
                ret.CallbackHeader[Headers.CorrelationSequence] = (
                    message.Origin.GetCorrelationSequence() + 1
                ).ToString(CultureInfo.InvariantCulture);

                if (message.Origin.Headers.TryGetValue(Headers.TraceParent, out var traceParent))
                {
                    ret.CallbackHeader[Headers.TraceParent] = traceParent;
                }

                await provider
                    .GetRequiredService<IOutboxPublisher>()
                    .PublishAsync(
                        ret.Result,
                        new PublishOptions { Topic = ret.CallbackName, Headers = ret.CallbackHeader },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException oce)
        {
            // Re-throw TaskCanceledException from handler timeouts (HttpClient, etc.)
            // so they propagate to _SetFailedState and are reported to the circuit breaker.
            if (oce is TaskCanceledException && !oce.CancellationToken.IsCancellationRequested)
            {
                var e = new SubscriberExecutionFailedException(LogSanitizer.Sanitize(oce.Message), oce);
                _TracingError(tracingTimestamp, message.Origin, descriptor.MethodInfo, e);
                e.ReThrow();
            }

            throw;
        }
        catch (Exception ex)
        {
            var e = new SubscriberExecutionFailedException(LogSanitizer.Sanitize(ex.Message), ex);

            _TracingError(tracingTimestamp, message.Origin, descriptor.MethodInfo, e);

            e.ReThrow();
        }
    }

    #region tracing

    private static long? _TracingBefore(Message message, MethodInfo method)
    {
        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforeSubscriberInvoke))
        {
            var eventData = new MessageEventDataSubExecute
            {
                OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Operation = message.GetName(),
                Message = message,
                MethodInfo = method,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforeSubscriberInvoke, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private static void _TracingAfter(long? tracingTimestamp, Message message, MethodInfo method)
    {
        MessageEventCounterSource.Log.WriteInvokeMetrics();
        if (
            tracingTimestamp != null
            && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterSubscriberInvoke)
        )
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var eventData = new MessageEventDataSubExecute
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                Message = message,
                MethodInfo = method,
                ElapsedTimeMs = now - tracingTimestamp.Value,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterSubscriberInvoke, eventData);
        }
    }

    private void _TracingError(long? tracingTimestamp, Message message, MethodInfo? method, Exception ex)
    {
        if (!_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorSubscriberInvoke))
        {
            return;
        }

        var now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

        var eventData = new MessageEventDataSubExecute
        {
            OperationTimestamp = now,
            Operation = message.GetName(),
            Message = message,
            MethodInfo = method,
            ElapsedTimeMs = now - tracingTimestamp,
            Exception = ex,
        };

        _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorSubscriberInvoke, eventData);
    }

    #endregion
}
