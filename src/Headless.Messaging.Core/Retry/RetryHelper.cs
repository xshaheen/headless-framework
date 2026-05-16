// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Retry;

/// <summary>
/// Shared retry decision logic used by consume and publish retry paths.
/// </summary>
/// <remarks>
/// <para>
/// Typical <see cref="IRetryBackoffStrategy.Compute"/> implementations return
/// <see cref="RetryDecision.Stop"/> or <see cref="RetryDecision.Continue"/> and let
/// <see cref="RecordAttemptAndComputeDecision"/> emit <see cref="RetryDecision.Exhausted"/> when the
/// configured <c>MaxInlineRetries</c>/<c>MaxPersistedRetries</c> budgets are consumed.
/// Strategies with their own attempt accounting MAY return <see cref="RetryDecision.Kind.Exhausted"/>
/// directly; this helper forwards it as a terminal exhaustion (Retries unchanged, <c>OnExhausted</c>
/// fires) — identical handling to the framework-emitted path.
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
    /// Upper bound applied to delays returned by <see cref="IRetryBackoffStrategy.Compute"/>.
    /// Guards against negative or overflowing values that would crash <see cref="Task.Delay(TimeSpan)"/>
    /// or overflow <see cref="DateTime"/> arithmetic when computing NextRetryAt.
    /// </summary>
    private static readonly TimeSpan _MaxDelay = TimeSpan.FromHours(24);

    /// <summary>
    /// Classifies a failed delivery attempt into <see cref="RetryDecision.Stop"/>,
    /// <see cref="RetryDecision.Exhausted"/>, or <see cref="RetryDecision.Continue"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method does NOT mutate <paramref name="message"/>. The persisted-pickup counter
    /// (<c>MediumMessage.Retries</c>) is advanced by the call site after consulting
    /// <see cref="ResolveNextState"/> — the increment happens only when the transition routes through
    /// persistence (inline budget consumed AND persisted budget remains).
    /// </para>
    /// <para>
    /// <see cref="RetryDecision.Kind.Exhausted"/> is returned only when BOTH the inline budget
    /// is consumed on the current dispatch (<c>inlineRetries + 1 &gt; policy.MaxInlineRetries</c>)
    /// AND the persisted budget is consumed (<c>message.Retries &gt;= policy.MaxPersistedRetries</c>).
    /// While inline budget remains the helper returns Continue so the inline retry loop can
    /// continue burst-retrying on the current pickup before terminal.
    /// </para>
    /// <para>
    /// Callers MUST pre-check cancellation via <see cref="IsCancellation"/> and return early before
    /// invoking this method. The helper does not re-check the token and will record an attempt
    /// against the retry budget even for shutdown-cancelled invocations if called.
    /// </para>
    /// </remarks>
    public static RetryDecision RecordAttemptAndComputeDecision(
        MediumMessage message,
        Exception exception,
        RetryPolicyOptions policy,
        int inlineRetries,
        ILogger? logger = null
    )
    {
        // Diagnostic guard: validator runs at startup only; post-startup mutation to null would
        // otherwise produce a bare NullReferenceException with no actionable context.
        Argument.IsNotNull(policy.BackoffStrategy);

        // Wrap the strategy call: a throwing custom strategy must not create an infinite
        // consumer-invocation loop. Treating a throw as Exhausted routes the row to terminal Failed
        // and invokes OnExhausted so the user is notified, instead of leaving the row in a state
        // that keeps getting re-picked.
        RetryDecision decision;

        try
        {
            decision = policy.BackoffStrategy.Compute(message.Retries, exception);
        }
        catch (Exception strategyEx)
        {
            logger?.BackoffStrategyThrew(strategyEx, message.StorageId, strategyEx.GetType().Name);
            return RetryDecision.Exhausted;
        }

        if (decision.Outcome == RetryDecision.Kind.Stop)
        {
            return RetryDecision.Stop;
        }

        if (decision.Outcome == RetryDecision.Kind.Exhausted)
        {
            return RetryDecision.Exhausted;
        }

        // Exhaust only when both axes are consumed. The inline retry loop will burst attempts
        // up to MaxInlineRetries on each pickup; the persisted retry processor will pick the row
        // up at most MaxPersistedRetries times after the initial dispatch. The two budgets compose
        // multiplicatively.
        if (!policy.HasMoreInlineAttempts(inlineRetries) && message.Retries >= policy.MaxPersistedRetries)
        {
            return RetryDecision.Exhausted;
        }

        // Strategy returned Continue: clamp the delay then surface it.
        // Defensive clamp because IRetryBackoffStrategy is a public-extension point and
        // custom strategies may return negative / overflowing values that would crash
        // Task.Delay or DateTime.Add. Non-finite TimeSpan values (NaN, ±Infinity) cannot be
        // constructed via TimeSpan.FromMilliseconds — the BCL rejects them at construction time
        // (ArgumentException / OverflowException). If a strategy tries to produce one, the strategy
        // call itself throws and the try/catch above routes it to Exhausted. So this clamp only
        // needs to handle finite-but-out-of-range values: Negative -> Zero; > 24h -> 24h.
        var clamped = decision.Delay;
        if (clamped < TimeSpan.Zero)
        {
            clamped = TimeSpan.Zero;
        }
        else if (clamped > _MaxDelay)
        {
            clamped = _MaxDelay;
        }

        return RetryDecision.Continue(clamped);
    }

    /// <summary>
    /// Returns <see langword="true"/> only when all three conditions hold: the
    /// <paramref name="cancellationToken"/> was cancelled, the exception is an
    /// <see cref="OperationCanceledException"/>, and its embedded token matches
    /// <paramref name="cancellationToken"/> exactly. Token-matching prevents treating
    /// a timeout OCE (e.g. from an inner <c>HttpClient</c> timeout) as a host-shutdown
    /// cancellation, which would suppress retries for transient failures.
    /// </summary>
    public static bool IsCancellation(Exception ex, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
        && ex is OperationCanceledException oce
        && oce.CancellationToken == cancellationToken;

    /// <summary>
    /// Invokes the supplied <paramref name="callback"/> with a hard timeout via
    /// <see cref="Task.WaitAsync(TimeSpan,CancellationToken)"/>.
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
        long storageId,
        ILogger logger,
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
                await callbackTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
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
        RetryDecision decision,
        int inlineRetries,
        RetryPolicyOptions policy,
        TimeProvider timeProvider,
        DateTime? currentNextRetryAt = null
    )
    {
        var isInlineRetryInFlight =
            decision.Outcome == RetryDecision.Kind.Continue && policy.HasMoreInlineAttempts(inlineRetries);

        var nextStatus = isInlineRetryInFlight ? StatusName.Scheduled : StatusName.Failed;

        if (decision.Outcome != RetryDecision.Kind.Continue)
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
        // crash recovery (the inline loop itself drives the actual Task.Delay). Push NextRetryAt
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
