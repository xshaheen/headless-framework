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
        var leased = await _LeaseAsync(message, cancellationToken).ConfigureAwait(false);
        if (!leased)
        {
            return (RetryDecision.Stop, OperateResult.Success);
        }

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

        // Mirror the failure path's SkippingOnExhaustedAlreadyTerminal log: when storage proves
        // the row is already terminal (typically Failed/NULL after a prior exhausted attempt),
        // surface the asymmetry for operators. Use CancellationToken.None so the must-complete
        // success-write semantics align with the publish-path's MessageSender._SetSuccessfulState.
        var updated = await dataStorage
            .ChangeReceiveStateAsync(message, StatusName.Succeeded, cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);

        if (!updated)
        {
            logger.SkippingSuccessfulAlreadyTerminal(message.StorageId);
        }

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
        // Pass the message's current NextRetryAt so ResolveNextState can preserve InitialDispatchGrace
        // on inline-in-flight transitions and pad the schedule against polling races.
        var state = RetryHelper.ResolveNextState(
            decision,
            inlineRetries,
            _retryPolicy,
            timeProvider,
            currentNextRetryAt: message.NextRetryAt
        );

        // Persist transition: inline budget consumed AND decision Continue means the call site
        // owns the Retries++ . The helper is pure with respect to MediumMessage; this is the only
        // place persisted-pickup count advances.
        if (decision.Outcome == RetryDecision.Kind.Continue && !state.IsInlineRetryInFlight)
        {
            var originalRetries = message.Retries;
            message.Retries++;
            return await _PersistFailedStateAsync(
                    message,
                    ex,
                    dispatchServices,
                    decision,
                    state,
                    originalRetries,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return await _PersistFailedStateAsync(
                message,
                ex,
                dispatchServices,
                decision,
                state,
                originalRetries: null,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task<RetryDecision> _PersistFailedStateAsync(
        MediumMessage message,
        Exception ex,
        IServiceProvider dispatchServices,
        RetryDecision decision,
        RetryNextState state,
        int? originalRetries,
        CancellationToken cancellationToken
    )
    {
        // Concurrent-Exhausted CAS guard: when two inverse-order pickups race to terminal-write,
        // the `Retries=@OriginalRetries` predicate inside ChangeReceiveStateAsync acts as the CAS
        // token. The first writer that wins sees `affected=true` and fires OnExhausted; the loser
        // sees `affected=false` and emits SkippingOnExhaustedAlreadyTerminal below. The per-row
        // Retries counter is the single source of truth — no extra CAS over the terminal-status
        // field is needed.
        //
        // Use CancellationToken.None for the terminal state write. If host shutdown fires between
        // computing the Exhausted decision and persisting it, we still want the row to land in its
        // terminal Failed/NULL state and OnExhausted to fire — otherwise on next pickup we'd re-invoke
        // the consumer as if the budget wasn't exhausted. The IsCancellation guard above already
        // short-circuited true host-shutdown OCEs; from this point on we are committed. Mirrors the
        // publish path (IMessageSender._SetFailedState) which makes the same choice for the same
        // reason.
        var affected = await dataStorage
            .ChangeReceiveStateAsync(
                message,
                state.NextStatus,
                nextRetryAt: state.NextRetryAt,
                originalRetries: originalRetries,
                cancellationToken: CancellationToken.None
            )
            .ConfigureAwait(false);

        if (affected && decision.Outcome == RetryDecision.Kind.Exhausted)
        {
            await _InvokeOnExhausted(message, ex, dispatchServices, cancellationToken).ConfigureAwait(false);
        }
        else if (!affected)
        {
            // Storage proves the row is already terminal — a redelivered already-exhausted message.
            // OnExhausted is skipped here; the log line is only emitted when the decision would
            // otherwise have fired the callback (Stop redeliveries never fire OnExhausted regardless,
            // so suppressing the log avoids noise for non-callback paths).
            if (decision.Outcome == RetryDecision.Kind.Exhausted)
            {
                logger.SkippingOnExhaustedAlreadyTerminal(message.StorageId);
            }

            return RetryDecision.Stop;
        }

        // Report the original (inner) exception to the circuit breaker so transient-classification
        // predicates see the real exception type, not the SubscriberExecutionFailedException wrapper.
        // Skip the report when the conditional UPDATE returned zero affected rows: that signals a
        // broker redelivery of an already-terminal row, not a fresh failure — counting it would
        // wrongly accumulate toward the breaker threshold.
        if (circuitBreakerStateManager is not null && affected)
        {
            var reportedException = ex is SubscriberExecutionFailedException { InnerException: { } inner } ? inner : ex;
            await circuitBreakerStateManager
                .ReportFailureAsync(message.Origin.GetGroup()!, reportedException)
                .ConfigureAwait(false);
        }

        return decision;
    }

    private async Task<bool> _LeaseAsync(MediumMessage message, CancellationToken cancellationToken)
    {
        var lockedUntil = timeProvider.GetUtcNow().UtcDateTime.Add(_retryPolicy.DispatchTimeout);
        return await dataStorage.LeaseReceiveAsync(message, lockedUntil, cancellationToken).ConfigureAwait(false);
    }

    private RetryDecision _UpdateMessageForRetry(MediumMessage message, Exception ex, int inlineRetries)
    {
        var decision = RetryHelper.RecordAttemptAndComputeDecision(
            message,
            ex,
            _retryPolicy,
            inlineRetries,
            logger: logger
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

        // Use the live dispatch scope so scoped services resolved here are the same
        // instances seen during ExecuteAsync. The caller (Dispatcher) is responsible for
        // creating and disposing that scope. RetryHelper.InvokeOnExhaustedAsync applies
        // the configured OnExhaustedTimeout and swallows handler exceptions.
        using var tenantScope = TenantContextScope.ChangeFromEnvelope(dispatchServices, message.Origin, logger);
        await RetryHelper
            .InvokeOnExhaustedAsync(
                callback,
                new FailedInfo
                {
                    ServiceProvider = dispatchServices,
                    MessageType = MessageType.Subscribe,
                    Message = message.Origin,
                    Exception = ex,
                },
                _retryPolicy.OnExhaustedTimeout,
                message.StorageId,
                logger,
                cancellationToken
            )
            .ConfigureAwait(false);
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
