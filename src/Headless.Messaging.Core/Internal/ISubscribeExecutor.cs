// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.MultiTenancy;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Messaging.Runtime;
using Headless.Reliability;
using Headless.UnitOfWork;
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
    /// when the retry budget exhausts. Transactional attempts own separate scopes that remain alive
    /// through commit or rollback; their services are not exposed to the exhausted callback.
    /// </param>
    /// <param name="descriptor">Optional consumer descriptor; resolved from the message name when omitted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<OperateResult> ExecuteAsync(
        MediumMessage message,
        IServiceProvider dispatchServices,
        ConsumerExecutorDescriptor? descriptor = null,
        CancellationToken cancellationToken = default
    );

    Task<OperateResult> ExecuteRetryAsync(
        MediumMessage message,
        IServiceProvider dispatchServices,
        RetryExecutionState executionState,
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
    ICircuitBreakerStateManager? circuitBreakerStateManager = null,
    MessagingTelemetry? telemetry = null
) : ISubscribeExecutor
{
    private readonly MessagingTelemetry _telemetry = telemetry ?? MessagingTelemetry.Default;
    private readonly string? _hostName = HostIdentity.GetInstanceHostname();
    private readonly MessagingOptions _options = options.Value;

    private readonly InboxMetricPolicy _inboxMetricPolicy =
        provider.GetService<InboxMetricPolicy>() ?? new InboxMetricPolicy(TenantTagName: null);

    private readonly IMessagingCapabilityModel? _capabilityModel = provider.GetService<IMessagingCapabilityModel>();
    private readonly RetryPolicyOptions _retryPolicy = options.Value.RetryPolicy;

    // Consume retries follow each consumer's failure policy, not RetryPolicyOptions.RetryStrategy: one pipeline with no
    // inline delay serves every consumer, and each execution supplies its consumer's classifier.
    private readonly MessagingConsumeRetryPipeline _retryPipeline = new(timeProvider);

    public Task<OperateResult> ExecuteAsync(
        MediumMessage message,
        IServiceProvider dispatchServices,
        ConsumerExecutorDescriptor? descriptor = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ExecuteAsync(message, dispatchServices, executionState: null, descriptor, cancellationToken);
    }

    public Task<OperateResult> ExecuteRetryAsync(
        MediumMessage message,
        IServiceProvider dispatchServices,
        RetryExecutionState executionState,
        ConsumerExecutorDescriptor? descriptor = null,
        CancellationToken cancellationToken = default
    )
    {
        return _ExecuteAsync(message, dispatchServices, executionState, descriptor, cancellationToken);
    }

    private async Task<OperateResult> _ExecuteAsync(
        MediumMessage message,
        IServiceProvider dispatchServices,
        RetryExecutionState? executionState,
        ConsumerExecutorDescriptor? descriptor,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(dispatchServices);

        if (descriptor == null)
        {
            var selector = provider.GetRequiredService<MethodMatcherCache>();
            var found = message.InboxKey is { } inboxKey
                ? selector.TryGetInboxExecutor(
                    inboxKey.ConsumerIdentity,
                    inboxKey.ContractIdentity,
                    inboxKey.ContractVersion,
                    inboxKey.Lane,
                    out descriptor
                )
                : message.Origin.GetConsumerIdentity() is { } consumerIdentity
                    && selector.TryGetConsumerIdentityExecutor(
                        message.Origin.Name,
                        consumerIdentity,
                        message.Lane,
                        out descriptor
                    );
            if (!found)
            {
                var safeName = LogSanitizer.Sanitize(message.Origin.Name);
                var safeConsumer = LogSanitizer.Sanitize(message.Origin.GetConsumerIdentity());

                logger.SubscriberNotFound(safeName, safeConsumer);

                var exception = new SubscriberNotFoundException(
                    $"Message (Name:{safeName},Consumer:{safeConsumer}) can not be found subscriber."
                        + $"{Environment.NewLine} Ensure a consumer with this identity is registered for the message."
                );

                // The consumer this row belongs to is no longer registered, so no attempt can ever succeed: the row is
                // a terminal failure. The terminal write is CAS-guarded and needs an active lease, which a row handed
                // over without one does not hold yet; a lost lease means another node owns the row, so stop.
                if (message.LockedUntil is null && !await _LeaseAsync(message, cancellationToken).ConfigureAwait(false))
                {
                    return OperateResult.Failed(exception);
                }

                // No subscriber.invoke span exists on the not-found path (BeforeSubscriberInvoke never ran), so
                // there is nothing to mark with error status; the failure is surfaced via _SetFailedState below.
                await _SetFailedState(
                        message,
                        exception,
                        dispatchServices,
                        new ConsumeRetryBudget(FailurePolicyDefinition.None),
                        MessagingRetryDecision.Exhausted,
                        budgetSpent: false,
                        executionState,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                return OperateResult.Failed(exception);
            }
        }

        //record instance id
        message.Origin.Headers[Headers.ExecutionInstanceId] = _hostName;

        var budget = new ConsumeRetryBudget(descriptor!.FailurePolicy);

        return await _retryPipeline
            .ExecuteAsync(
                (_, ct) => _ExecuteWithoutRetryAsync(message, descriptor, budget, dispatchServices, executionState, ct),
                (_, exception, _, ct) =>
                    _HandleRetryAsync(message, exception, dispatchServices, budget, executionState, ct),
                (_, exception, ct) =>
                    _HandleNonRetryableAsync(message, exception, dispatchServices, budget, executionState, ct),
                exception => _IsRetryable(exception, budget.Policy, message.StorageId),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Classifies a failed consume attempt. The handler's exceptions arrive wrapped, except a cancellation raised
    /// while the consume token is cancelled (host shutdown, dispatcher stop), which arrives raw. An unwrapped
    /// <see cref="OperationCanceledException"/> is never classified, because a cancelled dispatch writes nothing and
    /// the fail rules must not turn it into a terminal failure. A cancellation the handler raised on its own token
    /// (an HttpClient timeout, a CancelAfter) is wrapped and classified like any other failure. The built-in permanent
    /// set always fails. Otherwise the consumer's fail rules see the handler's own exception, unwrapped from the
    /// executor's wrapper exactly once, and anything they do not match is retried.
    /// </summary>
    private bool _IsRetryable(Exception exception, FailurePolicyDefinition policy, Guid storageId)
    {
        if (exception is OperationCanceledException)
        {
            return false;
        }

        if (RetryExceptionClassifier.IsPermanent(exception))
        {
            return false;
        }

        var effective = RetryExceptionClassifier.Unwrap(exception);
        if (!policy.ShouldFail(effective, out var ruleException))
        {
            return true;
        }

        if (ruleException is not null)
        {
            logger.FailurePolicyRuleThrew(ruleException, storageId, effective.GetType().Name);
        }

        return false;
    }

    private async Task<MessagingRetryAttempt> _ExecuteWithoutRetryAsync(
        MediumMessage message,
        ConsumerExecutorDescriptor descriptor,
        ConsumeRetryBudget budget,
        IServiceProvider dispatchServices,
        RetryExecutionState? executionState,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(message);

        cancellationToken.ThrowIfCancellationRequested();

        // A storage-returned lease is already acquired under the provider's authoritative clock.
        // Core must not reinterpret its expiry through the application clock; reservation and
        // state-write predicates validate the stored lease identity and activity.
        var needsLease = message.LockedUntil is null;

        // A crashed dispatch that already reserved its final attempt must not run another; a row already past its
        // consumer's budget (the policy shrank since it was scheduled) must not run at all. Both end through the
        // retry decision without invoking the handler, which bounds a crash loop to the consumer's budget.
        if (
            (
                RetryHelper.DetectCrashRecoveredReservation(message.Retries, message.InlineAttempts, budget)
                ?? RetryHelper.DetectBudgetOverrun(message.Retries, budget)
            ) is
            { } recoveryAttempt
        )
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
            logger.ConsumerExecuting(
                descriptor.ConsumerType.Name,
                descriptor.MethodName,
                descriptor.ResolvedConsumerIdentity
            );

            var sp = Stopwatch.StartNew();

            if (
                message.InboxKey is not null
                && _options.RequiredInboxCapability is MessagingInboxCapabilityTier.Transactional
            )
            {
                message.ExpiresAt = timeProvider.GetUtcNow().AddSeconds(_options.SucceedMessageExpiredAfter);
                // The runner and consumer must share the same DbContext, alive until commit or rollback.
                await using var attemptScope = dispatchServices.CreateAsyncScope();
                var attemptServices = attemptScope.ServiceProvider;
                var propagateTenant =
                    descriptor.MessageType is { } messageType
                    && attemptServices.GetService<IMiddlewareDescriptorRegistry>() is { } middlewareRegistry
                    && middlewareRegistry.TryGetConsumeDescriptors(
                        messageType,
                        descriptor.Lane,
                        out var middlewareDescriptors
                    )
                    && middlewareDescriptors.Any(m => m.MiddlewareType == typeof(TenantPropagationConsumeMiddleware));
                // Tenant-aware services can read the tenant at resolution, and auto-save runs after consume middleware.
                using var tenantScope = propagateTenant
                    ? TenantContextScope.ChangeFromEnvelope(attemptServices, message.Origin, logger)
                    : null;
                var transactionRunner = attemptServices.GetRequiredService<IInboxTransactionRunner>();
                await transactionRunner
                    .ExecuteAsync(
                        message,
                        (unitOfWork, ct) =>
                            _InvokeConsumerMethodAsync(message, descriptor, attemptServices, unitOfWork, ct),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                executionState?.RecordLeaseTransition(affected: true, lockedUntil: null);
                await _ReportSuccessfulStateAsync(message).ConfigureAwait(false);
            }
            else
            {
                await _InvokeConsumerMethodAsync(
                        message,
                        descriptor,
                        services: null,
                        unitOfWork: null,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                await _SetSuccessfulState(message, executionState).ConfigureAwait(false);
            }

            _RecordInboxMetric(message, InboxMetricKind.Terminal, InboxMetricOutcome.Succeeded);

            sp.Stop();

            MessageEventCounterSource.Log.WriteInvokeTimeMetrics(sp.Elapsed.TotalMilliseconds);
            if (logger.IsEnabled(LogLevel.Information))
            {
                var executionInstanceId = message.Origin.GetExecutionInstanceId();
                logger.ConsumerExecuted(
                    descriptor.ConsumerType.Name,
                    descriptor.MethodName,
                    descriptor.ResolvedConsumerIdentity,
                    sp.Elapsed.TotalMilliseconds,
                    executionInstanceId
                );
            }

            return MessagingRetryAttempt.Completed(OperateResult.Success);
        }
        catch (Exception ex)
            when (ex
                    is StaleInboxAttemptException
                        or UncommittedInboxCommitException
                        or IndeterminateInboxCommitException
            )
        {
            logger.ConsumerExecuteFailed(
                ex,
                LogSanitizer.Sanitize(message.Origin.Name),
                message.StorageId,
                message.Origin.GetExecutionInstanceId()
            );
            _ReleaseHalfOpenProbe(message);
            return MessagingRetryAttempt.Completed(OperateResult.Failed(ex));
        }
        catch (Exception ex) when (_IsDeserializationException(ex))
        {
            logger.ConsumerExecuteFailed(
                ex,
                LogSanitizer.Sanitize(message.Origin.Name),
                message.StorageId,
                message.Origin.GetExecutionInstanceId()
            );

            await _SetFailedState(
                    message,
                    ex,
                    dispatchServices,
                    budget,
                    MessagingRetryDecision.Exhausted,
                    budgetSpent: false,
                    executionState,
                    cancellationToken
                )
                .ConfigureAwait(false);

            return MessagingRetryAttempt.Completed(OperateResult.Failed(ex));
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

    private async ValueTask _SetSuccessfulState(MediumMessage message, RetryExecutionState? executionState)
    {
        // The cancellation token parameter is unused since F30 switched the storage write
        // to CancellationToken.None below. The method is private; the parameter is removed
        // outright rather than discarded.
        message.ExpiresAt = timeProvider.GetUtcNow().AddSeconds(_options.SucceedMessageExpiredAfter);

        // Mirror the failure path's SkippingOnExhaustedAlreadyTerminal log: when storage proves
        // the row is already terminal (typically Failed/NULL after a prior exhausted attempt),
        // surface the asymmetry for operators. Use CancellationToken.None so the must-complete
        // success-write semantics align with the publish-path's MessageSender._SetSuccessfulState.
        DateTimeOffset? lockedUntil = null;
        var updated = await dataStorage
            .ChangeReceiveRetryStateAsync(
                message,
                StatusName.Succeeded,
                // Unlike the publish path, the consume path DOES mutate Origin before invoking the
                // consumer: ExecuteAsync stamps Headers.ExecutionInstanceId on every attempt, after the
                // row was stored. Preserving here would drop that stamp from the persisted envelope.
                MessageContentWrite.Refresh,
                retryDelay: null,
                lockedUntil,
                originalRetries: message.Retries,
                originalInlineAttempts: message.InlineAttempts,
                cancellationToken: CancellationToken.None
            )
            .ConfigureAwait(false);
        executionState?.RecordLeaseTransition(updated, lockedUntil);

        if (!updated)
        {
            logger.SkippingSuccessfulAlreadyTerminal(message.StorageId);
        }

        await _ReportSuccessfulStateAsync(message).ConfigureAwait(false);
    }

    private async ValueTask _ReportSuccessfulStateAsync(MediumMessage message)
    {
        if (circuitBreakerStateManager is not null)
        {
            var circuitKey = CircuitBreakerKeys.For(message);
            await circuitBreakerStateManager.ReportSuccessAsync(circuitKey).ConfigureAwait(false);
        }
    }

    private async Task<bool> _HandleRetryAsync(
        MediumMessage message,
        Exception exception,
        IServiceProvider dispatchServices,
        ConsumeRetryBudget budget,
        RetryExecutionState? executionState,
        CancellationToken cancellationToken
    )
    {
        var decision = budget.Decide(message.Retries, _InlineRetriesCompleted(message));
        var persisted = await _SetFailedState(
                message,
                exception,
                dispatchServices,
                budget,
                decision,
                budgetSpent: true,
                executionState,
                cancellationToken
            )
            .ConfigureAwait(false);
        return persisted.Outcome == MessagingRetryDecision.Kind.Continue;
    }

    private async Task _HandleNonRetryableAsync(
        MediumMessage message,
        Exception exception,
        IServiceProvider dispatchServices,
        ConsumeRetryBudget budget,
        RetryExecutionState? executionState,
        CancellationToken cancellationToken
    )
    {
        // A fail rule or the built-in permanent set ends the message without spending the remaining retries. It is
        // still a terminal failure, so it fires OnExhausted like a spent budget; a host-shutdown cancellation never
        // gets that far because _SetFailedState writes nothing for it.
        await _SetFailedState(
                message,
                exception,
                dispatchServices,
                budget,
                MessagingRetryDecision.Exhausted,
                budgetSpent: false,
                executionState,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    // The durable counter counts reserved attempts, the first one included, so the retries already run are one less.
    private static int _InlineRetriesCompleted(MediumMessage message) => Math.Max(0, message.InlineAttempts - 1);

    private async Task<MessagingRetryDecision> _SetFailedState(
        MediumMessage message,
        Exception ex,
        IServiceProvider dispatchServices,
        ConsumeRetryBudget budget,
        MessagingRetryDecision decision,
        bool budgetSpent,
        RetryExecutionState? executionState,
        CancellationToken cancellationToken
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

        _LogRetryDecision(message, ex, decision, budget, budgetSpent);
        var originalInlineAttempts = message.InlineAttempts;

        message.Origin.AddOrUpdateException(ex);
        message.ExceptionInfo = ex.ExpandMessage();
        message.ExpiresAt = message.Added.AddSeconds(_options.FailedMessageExpiredAfter);

        // Inline-retry budget still available: persist as Scheduled/NULL so a crash mid-delay
        // leaves the row picked up by the polling query on restart (Failed/NULL is filtered out).
        // Only transition to Failed on terminal decisions (Stop, Exhausted) or when persisting
        // for the persisted-retry processor (Continue with inline budget exhausted, NextRetryAt set).
        var state = RetryHelper.ResolveNextState(
            decision,
            message.Retries,
            _InlineRetriesCompleted(message),
            budget,
            _retryPolicy.InitialDispatchGrace
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
                    executionState,
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
                executionState,
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
        RetryExecutionState? executionState,
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
                // _SetFailedState stamped the exception onto Origin, so the persisted envelope is stale
                // until this write refreshes it.
                MessageContentWrite.Refresh,
                state.NextRetry,
                lockedUntil,
                originalRetries,
                originalInlineAttempts,
                CancellationToken.None
            )
            .ConfigureAwait(false);
        executionState?.RecordLeaseTransition(affected, lockedUntil);

        if (affected && decision.Outcome == MessagingRetryDecision.Kind.Exhausted)
        {
            _RecordInboxMetric(message, InboxMetricKind.Terminal, InboxMetricOutcome.FailedExhausted);
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
            // Storage proves the row is already terminal — a redelivered message, or a racing writer
            // that won the terminal CAS. Every terminal consume failure (budget spent, fail rule,
            // built-in permanent, deserialization, unregistered consumer) fires OnExhausted once, from
            // the writer whose terminal write lands; this loser stops silently. The skip is logged only
            // for a terminal decision, so a lost non-terminal write adds no noise.
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
            var reportedException = RetryExceptionClassifier.Unwrap(ex);

            var circuitKey = CircuitBreakerKeys.For(message);
            await circuitBreakerStateManager
                .ReportFailureAsync(circuitKey, reportedException, cancellationToken)
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

        circuitBreakerStateManager.ReleaseHalfOpenProbe(CircuitBreakerKeys.For(message), message.ProbeEpoch);
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
        else
        {
            _RecordInboxMetric(message, InboxMetricKind.Attempt, InboxMetricOutcome.Reserved);
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
        else
        {
            _RecordInboxMetric(message, InboxMetricKind.Attempt, InboxMetricOutcome.Reserved);
        }

        return reserved;
    }

    private void _RecordInboxMetric(MediumMessage message, InboxMetricKind kind, InboxMetricOutcome outcome)
    {
        if (message.InboxKey is not { } key || _capabilityModel is null)
        {
            return;
        }

        var storageCapability = _capabilityModel.Providers.FirstOrDefault(capability =>
            capability.Role is MessagingProviderRole.Storage
        );
        if (storageCapability?.InboxCapability is not { } tier)
        {
            return;
        }

        MessagingMetrics.RecordInbox(
            kind,
            key.ConsumerIdentity,
            key.Lane,
            outcome,
            tier,
            storageCapability.Provider,
            message.Origin.Headers.TryGetValue(Headers.TenantId, out var tenantId) ? tenantId : null,
            _inboxMetricPolicy.TenantTagName
        );
    }

    private void _LogRetryDecision(
        MediumMessage message,
        Exception ex,
        MessagingRetryDecision decision,
        ConsumeRetryBudget budget,
        bool budgetSpent
    )
    {
        switch (decision.Outcome)
        {
            case MessagingRetryDecision.Kind.Stop:
            case MessagingRetryDecision.Kind.Exhausted when !budgetSpent:
                logger.StoredMessageNonRetryableFailure(message.StorageId, ex.GetType().Name);
                break;
            case MessagingRetryDecision.Kind.Exhausted:
                logger.ConsumerStoredMessageAfterThreshold(message.StorageId, budget.Policy.TotalAttempts);
                break;
            case MessagingRetryDecision.Kind.Continue:
                logger.ConsumerExecutionRetrying(message.StorageId, message.Retries);
                break;
        }
    }

    private async Task _InvokeConsumerMethodAsync(
        MediumMessage message,
        ConsumerExecutorDescriptor descriptor,
        IServiceProvider? services,
        IUnitOfWork? unitOfWork,
        CancellationToken cancellationToken
    )
    {
        var consumerContext = new ConsumerContext(descriptor, message, unitOfWork);
        var traceHandle = _TracingBefore(message.Origin, message.Lane, descriptor.MethodName, message.Retries);
        try
        {
            var ret = services is null
                ? await invoker.InvokeAsync(consumerContext, cancellationToken).ConfigureAwait(false)
                : await invoker.InvokeInScopeAsync(consumerContext, services, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(ret.CallbackName))
            {
                // TraceParent is NOT a reserved header, so it rides in PublishOptions.Headers alongside
                // any user-supplied AddResponseHeader keys. Lineage identifiers ARE reserved
                // (MessagePublishRequestFactory rejects them as custom headers), so they must flow through
                // the typed PublishOptions surface instead of CallbackHeader.
                if (message.Origin.Headers.TryGetValue(Headers.TraceParent, out var traceParent))
                {
                    ret.CallbackHeader ??= new Dictionary<string, string?>(StringComparer.Ordinal);
                    ret.CallbackHeader[Headers.TraceParent] = traceParent;
                }

                var callbackCorrelationId =
                    message.Origin.Headers.TryGetValue(Headers.CorrelationId, out var correlationId)
                    && !string.IsNullOrWhiteSpace(correlationId)
                        ? correlationId
                        : message.Origin.Id;
                var callbackCausationId = message.Origin.Id;
                var callbackSequence = message.Origin.GetCorrelationSequence() + 1;

                if (unitOfWork is not null)
                {
                    // Transactional tier: publish through the unit the inbox transaction runner enlisted and handed
                    // down, so the callback response joins the handler's transaction and is discarded when it
                    // rolls back. The autonomous IBus would leave the response behind.
                    await unitOfWork
                        .Outbox.PublishAsync(
                            ret.Result,
                            new OutboxOptions
                            {
                                MessageName = ret.CallbackName,
                                Headers = ret.CallbackHeader,
                                MessageType = ret.ResultType,
                                CorrelationId = callbackCorrelationId,
                                CausationId = callbackCausationId,
                                CorrelationSequence = callbackSequence,
                                // Chain the next hop: the published response carries this callback name so its
                                // consumer can react and publish a further response.
                                CallbackName = ret.ResponseCallbackName,
                            },
                            // callback response write must not be interrupted by shutdown — mirrors _SetSuccessfulState
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);
                }
                else
                {
                    // Non-transactional tier: no unit of work is bound here, and the bus publishes autonomously,
                    // so the callback response is a standalone durable write that outlives the dispatch scope.
                    await provider
                        .GetRequiredService<IBus>()
                        .PublishAsync(
                            ret.Result,
                            new PublishOptions
                            {
                                DeliveryMode = DeliveryMode.Durable,
                                MessageName = ret.CallbackName,
                                Headers = ret.CallbackHeader,
                                MessageType = ret.ResultType,
                                CorrelationId = callbackCorrelationId,
                                CausationId = callbackCausationId,
                                CorrelationSequence = callbackSequence,
                                CallbackName = ret.ResponseCallbackName,
                            },
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);
                }
            }

            // Fire the invoke success span only after the callback response publish completes so the success
            // event also reflects a successful callback publish; still fires on the no-callback path above.
            _TracingAfter(traceHandle, message.Origin.Name, descriptor.MethodName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Only the consume token decides that the dispatch was cancelled: an HttpClient timeout or a
            // handler-owned CancelAfter carries its own already-cancelled token, yet it is a handler failure and
            // falls to the wrapping catch below. A cancellation of ours propagates raw so nothing terminal is
            // written; stop the span un-errored so it is exported rather than leaked into Activity.Current.
            traceHandle.Activity?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            var e = new SubscriberExecutionFailedException(LogSanitizer.Sanitize(ex.Message), ex);

            _TracingError(traceHandle, message.Origin.Name, descriptor.MethodName, e);

            e.ReThrow();
        }
    }

    #region tracing

    private MessagingTraceHandle _TracingBefore(Message message, MessageLane lane, string method, int retryCount)
    {
        if (!MessagingDiagnostics.IsEnabled)
        {
            return default;
        }

        var now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var activity = _telemetry.SubscriberInvokeStart(message, message.Name, lane, method, retryCount, now);

        return new MessagingTraceHandle(activity, now);
    }

    private void _TracingAfter(MessagingTraceHandle traceHandle, string operation, string method)
    {
        MessageEventCounterSource.Log.WriteInvokeMetrics();

        if (!traceHandle.IsRecording)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        MessagingTelemetry.SubscriberInvokeStop(
            traceHandle.Activity,
            operation,
            method,
            traceHandle.StartTimestampMs!.Value,
            now
        );
    }

    private static void _TracingError(MessagingTraceHandle traceHandle, string operation, string method, Exception ex)
    {
        if (!traceHandle.IsRecording)
        {
            return;
        }

        MessagingTelemetry.SubscriberInvokeError(traceHandle.Activity, operation, method, ex);
    }

    #endregion
    private static bool _IsDeserializationException(Exception? ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is MessageDeserializationException)
            {
                return true;
            }
        }

        return false;
    }
}
