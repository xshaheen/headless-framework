// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Retry;

/// <summary>
/// Shared retry decision logic used by consume and publish retry paths.
/// </summary>
/// <remarks>
/// <para>
/// Polly's configured <c>RetryStrategyOptions.ShouldHandle</c> classifies the failure and its
/// delay configuration supplies the next retry delay. Messaging maps that runtime outcome into its
/// own durable scheduled or terminal state.
/// </para>
/// <para>
/// <c>MediumMessage.Retries</c> counts persisted-retry pickups only — inline iterations do not
/// advance it. The call site (not this helper) increments the counter when the resulting transition
/// is "persist for a later pickup". This helper is pure with respect to <see cref="MediumMessage"/>.
/// </para>
/// </remarks>
internal static class RetryHelper
{
    /// <summary>
    /// Returns <see langword="true"/> when the supplied <paramref name="cancellationToken"/>
    /// has been cancelled and <paramref name="ex"/> is an <see cref="OperationCanceledException"/>.
    /// The embedded token on the OCE is intentionally not required to match
    /// <paramref name="cancellationToken"/>: a linked CTS produced by
    /// <see cref="CancellationTokenSource.CreateLinkedTokenSource(CancellationToken)"/> carries
    /// the LINKED token on its OCE, not the outer token, so a strict identity check would
    /// mis-classify host-shutdown cancellations that arrive via a linked source.
    /// </summary>
    /// <remarks>
    /// The outer-token <c>IsCancellationRequested</c> guard still distinguishes shutdown OCEs
    /// from unrelated timeout OCEs (e.g. an <c>HttpClient</c> timeout fires its own OCE while the
    /// outer token is NOT cancelled, so this method returns <see langword="false"/> and the
    /// failure flows through the normal retry pipeline).
    /// </remarks>
    public static bool IsCancellation(Exception ex, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested && ex is OperationCanceledException;

    /// <summary>
    /// Detects a crash-recovered inline burst: the durable <see cref="MediumMessage.InlineAttempts"/>
    /// counter already reserved the final inline attempt before the process terminated, so no fresh
    /// attempt may run. Returns a synthetic retryable attempt (with classification bypassed) that
    /// routes the row to its persisted-retry or exhausted transition, or <see langword="null"/> when
    /// inline budget remains. Shared by the publish and consume paths so the threshold and message
    /// cannot drift.
    /// </summary>
    public static MessagingRetryAttempt? DetectCrashRecoveredReservation(
        int reservedInlineAttempts,
        RetryPolicyOptions policy
    )
    {
        if (reservedInlineAttempts < policy.RetryStrategy.MaxRetryAttempts + 1)
        {
            return null;
        }

        var recoveryException = new InvalidOperationException(
            "The process terminated after reserving the final inline delivery attempt."
        );

        return MessagingRetryAttempt.Retryable(OperateResult.Failed(recoveryException), bypassClassification: true);
    }

    /// <summary>
    /// Invokes the supplied <paramref name="callback"/> with a hard timeout via
    /// <see cref="Task.WaitAsync(TimeSpan,TimeProvider,CancellationToken)"/>.
    /// On timeout the callback is orphaned (continues running in the background) and a
    /// <c>OnExhaustedTimedOut</c> log event is emitted. Exceptions thrown by the callback
    /// are caught and logged so they cannot crash the dispatch loop. Cancellation observed
    /// on the supplied token is treated like any other callback exception — logged and absorbed.
    /// </summary>
    /// <remarks>
    /// On timeout, the linked CTS attached to the callback's <see cref="CancellationToken"/> is
    /// cancelled — this gives a cooperative callback a chance to short-circuit before the surrounding
    /// dispatch scope is disposed and <see cref="FailedInfo.ServiceProvider"/> becomes invalid. A
    /// callback that ignores its CT is still orphaned (we cannot abort an unmanaged Task), but the
    /// well-behaved case lands cleanly.
    /// </remarks>
    public static async Task InvokeOnExhaustedAsync(
        Func<FailedInfo, CancellationToken, Task> callback,
        FailedInfo failedInfo,
        TimeSpan timeout,
        Guid storageId,
        ILogger logger,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(callback);

        // #1 — own disposal explicitly. On timeout the callback Task is orphaned but still holds
        // callbackCts.Token. If we `using`-dispose the CTS here, any subsequent `ct.ThrowIf*` or
        // `Task.Delay(_, ct)` inside the orphan throws ObjectDisposedException instead of
        // OperationCanceledException, breaking the cooperative-cancel contract. Dispose the CTS
        // only after the orphan completes (fire-and-forget continuation).
        // CA2000 false-positive: disposal IS guaranteed via the try/finally `ctsOwned` flag below,
        // or transferred to the orphan-completion continuation.
#pragma warning disable CA2000
        var callbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
#pragma warning restore CA2000
        var ctsOwned = true;

        try
        {
            // Invoke via Task.Run-equivalent capture so a synchronous throw from the user callback
            // is materialized as a faulted Task — matching the original `await callback(...)` semantics
            // where the await harvests both synchronous and asynchronous exceptions. Without this,
            // a synchronously-throwing callback would escape outside the inner try and crash the
            // dispatch loop.
            Task callbackTask;
            try
            {
                callbackTask = callback(failedInfo, callbackCts.Token);
            }
            catch (Exception syncEx)
            {
                callbackTask = Task.FromException(syncEx);
            }

            try
            {
                await callbackTask.WaitAsync(timeout, timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                logger.OnExhaustedTimedOut(storageId, timeout.TotalSeconds);
                // Orphan warning: scope-bound services (FailedInfo.ServiceProvider) may become
                // invalid after the dispatch scope disposes. The orphaned callback continues running
                // in the background; an uncooperative callback that ignores the CT may race scope
                // disposal.
                logger.OnExhaustedCallbackOrphaned(storageId);

                // Signal the callback to stop touching scope-bound services. A cooperative callback
                // observes the token and unwinds; an uncooperative one is orphaned and may still
                // race the dispatch scope's disposal — documented as user responsibility.
                try
                {
                    await callbackCts.CancelAsync().ConfigureAwait(false);
                }
                catch (Exception cancelEx)
                {
                    logger.ExecutedThresholdCallbackFailed(cancelEx, LogSanitizer.Sanitize(cancelEx.Message));
                }

                // Hand ownership of CTS disposal to a continuation that fires when the orphan
                // completes. This ensures a cooperative callback observes OCE (not
                // ObjectDisposedException) when it checks the token after timeout.
                var localCts = callbackCts;
                _ = callbackTask.ContinueWith(
                    _ => localCts.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
                ctsOwned = false;
            }
            catch (OperationCanceledException oce)
                when (cancellationToken.IsCancellationRequested
                    && (oce.CancellationToken == cancellationToken || oce.CancellationToken == callbackCts.Token)
                )
            {
                // #13 — Host shutdown observed during WaitAsync. The OCE may carry either the outer
                // host token (when WaitAsync raises it directly) or the inner linked callbackCts.Token
                // (when a cooperative callback awaits something tied to its CT and the linked CTS
                // fires first). Both indicate shutdown — accept either token-identity.
                // Cancel the linked CTS so a cooperative callback sees cancellation and unwinds
                // cleanly instead of touching a disposed scope. Not a callback fault — debug log only.
                logger.OnExhaustedCallbackCancelledAtShutdown(storageId);
                try
                {
                    await callbackCts.CancelAsync().ConfigureAwait(false);
                }
#pragma warning disable ERP022  // Best-effort: a throw here would only mask the shutdown signal — swallow.
                catch
                {
                    // ignored
                }
#pragma warning restore ERP022
            }
            catch (Exception callbackEx)
            {
                logger.ExecutedThresholdCallbackFailed(callbackEx, LogSanitizer.Sanitize(callbackEx.Message));
            }
        }
        finally
        {
            if (ctsOwned)
            {
                callbackCts.Dispose();
            }
        }
    }

    /// <summary>
    /// End-to-end <c>OnExhausted</c> invocation: enters the message's tenant context, builds the
    /// <see cref="FailedInfo"/> envelope around the configured callback, and applies the configured
    /// timeout / cancellation contract. Both the publish path (<c>MessageSender</c>) and the consume
    /// path (<c>SubscribeExecutor</c>) share this body; the only per-path knob is
    /// <paramref name="messageType"/>.
    /// </summary>
    /// <remarks>
    /// Returns immediately when <see cref="RetryPolicyOptions.OnExhausted"/> is <see langword="null"/>.
    /// All callback failures (synchronous throw, async fault, timeout, shutdown) are absorbed via
    /// <see cref="InvokeOnExhaustedAsync"/> — see that method for the cancellation/timeout contract.
    /// </remarks>
    public static async Task RunOnExhaustedAsync(
        RetryPolicyOptions policy,
        MediumMessage message,
        Exception exception,
        IServiceProvider dispatchServices,
        MessageType messageType,
        ILogger logger,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        var callback = policy.OnExhausted;
        if (callback is null)
        {
            return;
        }

        // Use the live dispatch scope so scoped services resolved by the callback are the same
        // instances seen during the consume/send attempt. The caller (Dispatcher) owns this scope.
        using var tenantScope = TenantContextScope.ChangeFromEnvelope(dispatchServices, message.Origin, logger);
        await InvokeOnExhaustedAsync(
                callback,
                new FailedInfo
                {
                    ServiceProvider = dispatchServices,
                    MessageType = messageType,
                    Message = message.Origin,
                    IntentType = message.IntentType,
                    Exception = exception,
                    StorageId = message.StorageId,
                    RetryCount = message.Retries,
                },
                policy.OnExhaustedTimeout,
                message.StorageId,
                logger,
                timeProvider,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Derives the persistence state from a retry decision and inline-retry counters.
    /// Both the consume path (SubscribeExecutor) and the publish path (MessageSender) share
    /// identical logic; a single definition prevents the two from drifting.
    /// </summary>
    /// <param name="currentNextRetryAt">
    /// The message's current <c>NextRetryAt</c> from storage. Used to pick the LATER of
    /// (the inline-retry resume time + safety margin) and the existing schedule, so that
    /// <c>InitialDispatchGrace</c> on a freshly-stored message is not lowered by a smaller
    /// inline delay (and so the polling query cannot race the inline-retry mid-sleep).
    /// </param>
    public static RetryNextState ResolveNextState(
        MessagingRetryDecision decision,
        int inlineRetries,
        RetryPolicyOptions policy,
        TimeProvider timeProvider,
        DateTime? currentNextRetryAt = null
    )
    {
        // #1 — when Polly returns Delay >= DispatchTimeout, the inline retry burst ends early
        // the Polly pipeline must hand control to the persisted-retry path without sleeping.
        // Otherwise the call site would skip the Retries++ increment (gated on
        // !IsInlineRetryInFlight), the row would sit with NextRetryAt set but Retries unchanged,
        // and the persisted budget would never be consumed — OnExhausted would never fire.
        var inlineBudgetWouldOversleep =
            decision.Outcome == MessagingRetryDecision.Kind.Continue && decision.Delay >= policy.DispatchTimeout;

        var isInlineRetryInFlight =
            decision.Outcome == MessagingRetryDecision.Kind.Continue
            && policy.HasMoreInlineAttempts(inlineRetries)
            && !inlineBudgetWouldOversleep;

        var nextStatus = isInlineRetryInFlight ? StatusName.Scheduled : StatusName.Failed;

        if (decision.Outcome != MessagingRetryDecision.Kind.Continue)
        {
            // Stop / Exhausted: clear NextRetryAt so the row is terminal and excluded from pickup.
            return new RetryNextState(isInlineRetryInFlight, NextRetryAt: null, nextStatus);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var inlineResumeAt = now.Add(decision.Delay);

        if (!isInlineRetryInFlight)
        {
            // Persisted-retry transition: NextRetryAt drives when the retry processor picks up
            // the row, so it MUST equal the strategy's delay (no padding, no preservation).
            return new RetryNextState(isInlineRetryInFlight, inlineResumeAt, nextStatus);
        }

        // Inline-retry in-flight transition: the persisted NextRetryAt only matters for
        // crash recovery (the Polly pipeline itself drives the actual delay). Push NextRetryAt
        // past the inline-retry resume point by InitialDispatchGrace so the polling cycle does
        // not race the inline path mid-sleep, AND preserve any existing schedule that is later
        // (e.g., InitialDispatchGrace from initial store).
        var paddedResume = inlineResumeAt.Add(policy.InitialDispatchGrace);
        var nextRetryAt = currentNextRetryAt is { } existing && existing > paddedResume ? existing : paddedResume;

        return new RetryNextState(isInlineRetryInFlight, nextRetryAt, nextStatus);
    }
}

/// <summary>
/// Value returned by <see cref="RetryHelper.ResolveNextState"/> describing the persistence state
/// for a single failed delivery attempt.
/// </summary>
internal readonly record struct RetryNextState(
    bool IsInlineRetryInFlight,
    DateTime? NextRetryAt,
    StatusName NextStatus
);
