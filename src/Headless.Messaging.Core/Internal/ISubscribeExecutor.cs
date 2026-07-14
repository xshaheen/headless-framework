// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Headless.Checks;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Messaging.Runtime;
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
    /// <param name="descriptor">Optional consumer descriptor; resolved from the message name when omitted.</param>
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

    private readonly string? _hostName = HostIdentity.GetInstanceHostname();
    private readonly MessagingOptions _options = options.Value;
    private readonly RetryPolicyOptions _retryPolicy = options.Value.RetryPolicy;
    private readonly MessagingRetryPipeline _retryPipeline = new(options.Value.RetryPolicy, timeProvider, logger);

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
            if (
                !selector.TryGetMessageNameExecutor(
                    message.Origin.Name,
                    message.Origin.GetGroup()!,
                    message.IntentType,
                    out descriptor
                )
            )
            {
                var safeName = LogSanitizer.Sanitize(message.Origin.Name);
                var safeGroup = LogSanitizer.Sanitize(message.Origin.GetGroup());

                logger.SubscriberNotFound(safeName, safeGroup);

                var exception = new SubscriberNotFoundException(
                    $"Message (Name:{safeName},Group:{safeGroup}) can not be found subscriber."
                        + $"{Environment.NewLine} Ensure the subscriber method is decorated with [Subscribe] and the consumer group matches."
                );

                _TracingError(
                    timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                    message.Origin,
                    message.IntentType,
                    method: null,
                    exception,
                    retryCount: 0,
                    cancellationToken
                );

                await _SetFailedState(
                        message,
                        exception,
                        dispatchServices,
                        decision: MessagingRetryDecision.Stop,
                        cancellationToken: cancellationToken
                    )
                    .ConfigureAwait(false);
                return OperateResult.Failed(exception);
            }
        }

        //record instance id
        message.Origin.Headers[Headers.ExecutionInstanceId] = _hostName;

        return await _retryPipeline
            .ExecuteAsync(
                (inlineRetries, ct) => _ExecuteWithoutRetryAsync(message, descriptor, inlineRetries, ct),
                (inlineRetries, exception, delay, strategyFailed, ct) =>
                    _HandleRetryAsync(message, exception, dispatchServices, inlineRetries, delay, strategyFailed, ct),
                (inlineRetries, exception, ct) =>
                    _HandleNonRetryableAsync(message, exception, dispatchServices, inlineRetries, ct),
                message.StorageId,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task<MessagingRetryAttempt> _ExecuteWithoutRetryAsync(
        MediumMessage message,
        ConsumerExecutorDescriptor descriptor,
        int inlineRetries,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(message);

        cancellationToken.ThrowIfCancellationRequested();

        // R6 — skip the redundant lease call when the atomic-claim pickup has already leased the
        // row. The pickup query (GetReceivedMessagesOfNeedRetryAsync) writes
        // `LockedUntil = now + DispatchTimeout` in the same UPDATE that returns the row, so any
        // message arriving here with a future LockedUntil was leased less than a few milliseconds
        // ago and re-leasing only inflates the rolling-restart retry-gap upper bound by the queue
        // delay. Fresh transport dispatches (LockedUntil null or expired) still take the lease.
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var needsLease = message.LockedUntil is not { } lockedUntil || lockedUntil <= now;

        inlineRetries = message.InlineAttempts;
        if (RetryHelper.DetectCrashRecoveredReservation(inlineRetries, _retryPolicy) is { } recoveryAttempt)
        {
            // The recovery transition still writes CAS-guarded state, which requires an active
            // lease — take a plain lease (no fresh reservation; the crashed reservation is spent).
            if (needsLease && !await _LeaseAsync(message, cancellationToken).ConfigureAwait(false))
            {
                _ReleaseHalfOpenProbe(message);
                return MessagingRetryAttempt.Completed(OperateResult.Success);
            }

            return recoveryAttempt;
        }

        if (needsLease)
        {
            // Fresh-dispatch fast path: acquire the lease AND durably reserve the first attempt in
            // one statement instead of two sequential writes (lease + reserve) on the same row.
            if (!await _LeaseAndReserveAttemptAsync(message, cancellationToken).ConfigureAwait(false))
            {
                _ReleaseHalfOpenProbe(message);
                return MessagingRetryAttempt.Completed(OperateResult.Success);
            }
        }
        else if (!await _ReserveAttemptAsync(message, cancellationToken).ConfigureAwait(false))
        {
            _ReleaseHalfOpenProbe(message);
            return MessagingRetryAttempt.Completed(OperateResult.Success);
        }

        try
        {
            logger.ConsumerExecuting(descriptor.ImplTypeInfo.Name, descriptor.MethodInfo.Name, descriptor.GroupName);

            var sp = Stopwatch.StartNew();

            await _InvokeConsumerMethodAsync(message, descriptor, cancellationToken).ConfigureAwait(false);

            sp.Stop();

            await _SetSuccessfulState(message).ConfigureAwait(false);

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

            return MessagingRetryAttempt.Completed(OperateResult.Success);
        }
        catch (Exception ex)
        {
            logger.ConsumerExecuteFailed(
                ex,
                LogSanitizer.Sanitize(message.Origin.Name),
                message.StorageId,
                message.Origin.GetExecutionInstanceId()
            );

            return MessagingRetryAttempt.Retryable(OperateResult.Failed(ex));
        }
    }

    private async ValueTask _SetSuccessfulState(MediumMessage message)
    {
        // R8 — the cancellation token parameter is unused since F30 switched the storage write
        // to CancellationToken.None below. The method is private; the parameter is removed
        // outright rather than discarded.
        message.ExpiresAt = timeProvider.GetUtcNow().UtcDateTime.AddSeconds(_options.SucceedMessageExpiredAfter);

        // Mirror the failure path's SkippingOnExhaustedAlreadyTerminal log: when storage proves
        // the row is already terminal (typically Failed/NULL after a prior exhausted attempt),
        // surface the asymmetry for operators. Use CancellationToken.None so the must-complete
        // success-write semantics align with the publish-path's MessageSender._SetSuccessfulState.
        var updated = await dataStorage
            .ChangeReceiveRetryStateAsync(
                message,
                StatusName.Succeeded,
                nextRetryAt: null,
                lockedUntil: null,
                originalRetries: message.Retries,
                originalInlineAttempts: message.InlineAttempts,
                cancellationToken: CancellationToken.None
            )
            .ConfigureAwait(false);

        if (!updated)
        {
            logger.SkippingSuccessfulAlreadyTerminal(message.StorageId);
        }

        if (circuitBreakerStateManager is not null)
        {
            var circuitBreakerGroup = CircuitBreakerGroupKeys.For(message);
            await circuitBreakerStateManager.ReportSuccessAsync(circuitBreakerGroup).ConfigureAwait(false);
        }
    }

    private async Task<bool> _HandleRetryAsync(
        MediumMessage message,
        Exception exception,
        IServiceProvider dispatchServices,
        int _,
        TimeSpan delay,
        bool strategyFailed,
        CancellationToken cancellationToken
    )
    {
        var inlineRetries = Math.Max(0, message.InlineAttempts - 1);
        var decision =
            strategyFailed ? MessagingRetryDecision.Exhausted
            : !_retryPolicy.HasMoreInlineAttempts(inlineRetries) && message.Retries >= _retryPolicy.MaxPersistedRetries
                ? MessagingRetryDecision.Exhausted
            : MessagingRetryDecision.Continue(delay);
        var persisted = await _SetFailedState(
                message,
                exception,
                dispatchServices,
                inlineRetries,
                decision,
                cancellationToken
            )
            .ConfigureAwait(false);
        return persisted.Outcome == MessagingRetryDecision.Kind.Continue;
    }

    private async Task _HandleNonRetryableAsync(
        MediumMessage message,
        Exception exception,
        IServiceProvider dispatchServices,
        int _,
        CancellationToken cancellationToken
    )
    {
        var inlineRetries = Math.Max(0, message.InlineAttempts - 1);
        await _SetFailedState(
                message,
                exception,
                dispatchServices,
                inlineRetries,
                MessagingRetryDecision.Stop,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task<MessagingRetryDecision> _SetFailedState(
        MediumMessage message,
        Exception ex,
        IServiceProvider dispatchServices,
        int inlineRetries = 0,
        MessagingRetryDecision decision = default,
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
            _ReleaseHalfOpenProbe(message);
            return MessagingRetryDecision.Stop;
        }

        _LogRetryDecision(message, ex, decision);
        var originalInlineAttempts = message.InlineAttempts;

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
        if (decision.Outcome == MessagingRetryDecision.Kind.Continue && !state.IsInlineRetryInFlight)
        {
            var originalRetries = message.Retries;
            message.Retries++;
            message.InlineAttempts = 0;
            await _PersistFailedStateAsync(
                    message,
                    ex,
                    dispatchServices,
                    decision,
                    state,
                    originalRetries,
                    originalInlineAttempts,
                    cancellationToken
                )
                .ConfigureAwait(false);
            return MessagingRetryDecision.Stop;
        }

        // #7 — enforce the storage CAS predicate on every state-write site (including inline-in-flight
        // and terminal transitions). The Retries counter doesn't advance on those paths, so the
        // witness is just the current value; storage will accept the write only if another writer
        // hasn't bumped Retries (or won the terminal-race) since this dispatch read the row.
        return await _PersistFailedStateAsync(
                message,
                ex,
                dispatchServices,
                decision,
                state,
                originalRetries: message.Retries,
                originalInlineAttempts,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task<MessagingRetryDecision> _PersistFailedStateAsync(
        MediumMessage message,
        Exception ex,
        IServiceProvider dispatchServices,
        MessagingRetryDecision decision,
        RetryNextState state,
        int originalRetries,
        int originalInlineAttempts,
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
        //
        // #14 — Preserve the active pickup lease on inline-in-flight transitions. Without an
        // explicit `lockedUntil`, the storage default of NULL would clear the row's lease mid-burst,
        // making the row eligible for pickup by the retry processor while the inline retry burst is
        // still mid-sleep. Persisted-retry transitions DO clear the lease (lockedUntil: null) so the
        // row can be re-picked.
        var lockedUntil = state.IsInlineRetryInFlight ? message.LockedUntil : null;
        var affected = await dataStorage
            .ChangeReceiveRetryStateAsync(
                message,
                state.NextStatus,
                state.NextRetryAt,
                lockedUntil,
                originalRetries,
                originalInlineAttempts,
                CancellationToken.None
            )
            .ConfigureAwait(false);

        if (affected && decision.Outcome == MessagingRetryDecision.Kind.Exhausted)
        {
            // #6 — shared OnExhausted body lives in RetryHelper; only MessageType varies per path.
            await RetryHelper
                .RunOnExhaustedAsync(
                    _retryPolicy,
                    message,
                    ex,
                    dispatchServices,
                    MessageType.Subscribe,
                    logger,
                    timeProvider,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        else if (!affected)
        {
            // Storage proves the row is already terminal — a redelivered already-exhausted message.
            // OnExhausted is skipped here; the log line is only emitted when the decision would
            // otherwise have fired the callback (Stop redeliveries never fire OnExhausted regardless,
            // so suppressing the log avoids noise for non-callback paths).
            if (decision.Outcome == MessagingRetryDecision.Kind.Exhausted)
            {
                logger.SkippingOnExhaustedAlreadyTerminal(message.StorageId);
            }

            _ReleaseHalfOpenProbe(message);
            return MessagingRetryDecision.Stop;
        }

        // Report the original (inner) exception to the circuit breaker so transient-classification
        // predicates see the real exception type, not the SubscriberExecutionFailedException wrapper.
        // Skip the report when the conditional UPDATE returned zero affected rows: that signals a
        // broker redelivery of an already-terminal row, not a fresh failure — counting it would
        // wrongly accumulate toward the breaker threshold.
        if (circuitBreakerStateManager is not null && affected)
        {
            var reportedException = ex is SubscriberExecutionFailedException { InnerException: { } inner } ? inner : ex;

            var circuitBreakerGroup = CircuitBreakerGroupKeys.For(message);
            await circuitBreakerStateManager
                .ReportFailureAsync(circuitBreakerGroup, reportedException, cancellationToken)
                .ConfigureAwait(false);
        }

        return decision;
    }

    private void _ReleaseHalfOpenProbe(MediumMessage message)
    {
        if (circuitBreakerStateManager is null)
        {
            return;
        }

        circuitBreakerStateManager.ReleaseHalfOpenProbe(CircuitBreakerGroupKeys.For(message));
    }

    private async Task<bool> _LeaseAsync(MediumMessage message, CancellationToken cancellationToken)
    {
        return await dataStorage
            .LeaseReceiveAsync(message, _retryPolicy.DispatchTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> _LeaseAndReserveAttemptAsync(MediumMessage message, CancellationToken cancellationToken)
    {
        var originalInlineAttempts = message.InlineAttempts;
        message.InlineAttempts++;
        var reserved = await dataStorage
            .LeaseReceiveAndReserveAttemptAsync(
                message,
                _retryPolicy.DispatchTimeout,
                originalInlineAttempts,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (!reserved)
        {
            message.InlineAttempts = originalInlineAttempts;
        }

        return reserved;
    }

    private async Task<bool> _ReserveAttemptAsync(MediumMessage message, CancellationToken cancellationToken)
    {
        var originalInlineAttempts = message.InlineAttempts;
        message.InlineAttempts++;
        var reserved = await dataStorage
            .ReserveReceiveAttemptAsync(message, originalInlineAttempts, cancellationToken)
            .ConfigureAwait(false);
        if (!reserved)
        {
            message.InlineAttempts = originalInlineAttempts;
        }

        return reserved;
    }

    private void _LogRetryDecision(MediumMessage message, Exception ex, MessagingRetryDecision decision)
    {
        switch (decision.Outcome)
        {
            case MessagingRetryDecision.Kind.Stop:
                logger.StoredMessageNonRetryableFailure(message.StorageId, ex.GetType().Name);
                break;
            case MessagingRetryDecision.Kind.Exhausted:
                logger.ConsumerStoredMessageAfterThreshold(message.StorageId, _retryPolicy.MaxPersistedRetries);
                break;
            case MessagingRetryDecision.Kind.Continue:
                logger.ConsumerExecutionRetrying(message.StorageId, message.Retries);
                break;
        }
    }

    private async Task _InvokeConsumerMethodAsync(
        MediumMessage message,
        ConsumerExecutorDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        var consumerContext = new ConsumerContext(descriptor, message);
        var tracingTimestamp = _TracingBefore(
            message.Origin,
            message.IntentType,
            descriptor.MethodInfo,
            message.Retries,
            cancellationToken
        );
        try
        {
            var ret = await invoker.InvokeAsync(consumerContext, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(ret.CallbackName))
            {
                // TraceParent is NOT a reserved header, so it rides in PublishOptions.Headers alongside
                // any user-supplied AddResponseHeader keys. The correlation identifiers ARE reserved
                // (MessagePublishRequestFactory rejects them as custom headers), so they must flow through
                // the typed PublishOptions surface instead of CallbackHeader.
                if (message.Origin.Headers.TryGetValue(Headers.TraceParent, out var traceParent))
                {
                    ret.CallbackHeader ??= new Dictionary<string, string?>(StringComparer.Ordinal);
                    ret.CallbackHeader[Headers.TraceParent] = traceParent;
                }

                await provider
                    .GetRequiredService<IOutboxBus>()
                    .PublishAsync(
                        ret.Result,
                        new PublishOptions
                        {
                            MessageName = ret.CallbackName,
                            Headers = ret.CallbackHeader,
                            MessageType = ret.ResultType,
                            CorrelationId = message.Origin.Id,
                            CorrelationSequence = message.Origin.GetCorrelationSequence() + 1,
                            // Chain the next hop: the published response carries this callback name so its
                            // consumer can react and publish a further response.
                            CallbackName = ret.ResponseCallbackName,
                        },
                        // callback response write must not be interrupted by shutdown — mirrors _SetSuccessfulState
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }

            // Fire AfterSubscriberInvoke only after the callback response publish completes so the success
            // event also reflects a successful callback publish; still fires on the no-callback path above.
            _TracingAfter(
                tracingTimestamp,
                message.Origin,
                message.IntentType,
                descriptor.MethodInfo,
                message.Retries,
                cancellationToken
            );
        }
        catch (OperationCanceledException oce)
        {
            // Re-throw TaskCanceledException from handler timeouts (HttpClient, etc.)
            // so they propagate to _SetFailedState and are reported to the circuit breaker.
            if (oce is TaskCanceledException && !oce.CancellationToken.IsCancellationRequested)
            {
                var e = new SubscriberExecutionFailedException(LogSanitizer.Sanitize(oce.Message), oce);
                _TracingError(
                    tracingTimestamp,
                    message.Origin,
                    message.IntentType,
                    descriptor.MethodInfo,
                    e,
                    message.Retries,
                    cancellationToken
                );
                e.ReThrow();
            }

            throw;
        }
        catch (Exception ex)
        {
            var e = new SubscriberExecutionFailedException(LogSanitizer.Sanitize(ex.Message), ex);

            _TracingError(
                tracingTimestamp,
                message.Origin,
                message.IntentType,
                descriptor.MethodInfo,
                e,
                message.Retries,
                cancellationToken
            );

            e.ReThrow();
        }
    }

    #region tracing

    private long? _TracingBefore(
        Message message,
        IntentType intentType,
        MethodInfo method,
        int retryCount,
        CancellationToken cancellationToken
    )
    {
        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforeSubscriberInvoke))
        {
            var eventData = new MessageEventDataSubExecute
            {
                OperationTimestamp = timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                Operation = message.Name,
                Message = message,
                IntentType = intentType,
                MethodInfo = method,
                RetryCount = retryCount,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforeSubscriberInvoke, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private void _TracingAfter(
        long? tracingTimestamp,
        Message message,
        IntentType intentType,
        MethodInfo method,
        int retryCount,
        CancellationToken cancellationToken
    )
    {
        MessageEventCounterSource.Log.WriteInvokeMetrics();
        if (
            tracingTimestamp != null
            && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterSubscriberInvoke)
        )
        {
            var now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            var eventData = new MessageEventDataSubExecute
            {
                OperationTimestamp = now,
                Operation = message.Name,
                Message = message,
                IntentType = intentType,
                MethodInfo = method,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                RetryCount = retryCount,
                CancellationToken = cancellationToken,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterSubscriberInvoke, eventData);
        }
    }

    private void _TracingError(
        long? tracingTimestamp,
        Message message,
        IntentType intentType,
        MethodInfo? method,
        Exception ex,
        int retryCount,
        CancellationToken cancellationToken
    )
    {
        if (!_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorSubscriberInvoke))
        {
            return;
        }

        var now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

        var eventData = new MessageEventDataSubExecute
        {
            OperationTimestamp = now,
            Operation = message.Name,
            Message = message,
            IntentType = intentType,
            MethodInfo = method,
            ElapsedTimeMs = now - tracingTimestamp,
            Exception = ex,
            RetryCount = retryCount,
            CancellationToken = cancellationToken,
        };

        _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorSubscriberInvoke, eventData);
    }

    #endregion
}
