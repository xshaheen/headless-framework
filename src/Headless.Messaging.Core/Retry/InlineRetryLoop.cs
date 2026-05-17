// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;

namespace Headless.Messaging.Retry;

/// <summary>
/// Drives the inline-retry loop shared by the consume (<c>SubscribeExecutor</c>) and publish
/// (<c>MessageSender</c>) paths. The caller-provided attempt function performs one delivery
/// attempt and reports both the retry decision (Stop / Exhausted / Continue) and the outcome
/// value. The loop continues only while the decision is <see cref="RetryDecision.Kind.Continue"/>
/// and the inline-retry budget is not exhausted.
/// </summary>
internal static class InlineRetryLoop
{
    /// <summary>
    /// Runs <paramref name="attemptFn"/> until it returns a terminal decision or the inline-retry
    /// budget is exhausted. The <c>inlineRetries</c> counter passed to <paramref name="attemptFn"/>
    /// starts at zero on the first call and increments after each retry. The inter-attempt delay
    /// is driven through <paramref name="timeProvider"/> so tests can advance time deterministically
    /// via <c>FakeTimeProvider</c>.
    /// </summary>
    public static async Task<TResult> ExecuteAsync<TResult>(
        Func<int, CancellationToken, Task<(RetryDecision Decision, TResult Result)>> attemptFn,
        RetryPolicyOptions policy,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        var inlineRetries = 0;

        while (true)
        {
            var (decision, result) = await attemptFn(inlineRetries, cancellationToken).ConfigureAwait(false);

            if (decision.Outcome != RetryDecision.Kind.Continue)
            {
                return result;
            }

            // Check budget BEFORE incrementing so the predicate matches its name (the count of
            // inline retries already consumed). Equivalent to the previous post-increment
            // `inlineRetries > MaxInlineRetries` check.
            if (!policy.HasMoreInlineAttempts(inlineRetries))
            {
                return result;
            }

            // #21 — runtime-guard for backoff delays that exceed the dispatch timeout. The pickup
            // lease is sized to DispatchTimeout, so a strategy that returns Delay >= DispatchTimeout
            // would have the inline loop snooze past the lease boundary, allowing another replica
            // to re-pick the row mid-sleep and double-dispatch. Transition to persisted-retry
            // (return the current decision, let the caller persist as Continue with NextRetryAt set)
            // by exiting the inline loop early.
            if (decision.Delay >= policy.DispatchTimeout)
            {
                return result;
            }

            inlineRetries++;

            // Defend against a zero-delay strategy spinning past cancellation between attempts:
            // timeProvider.Delay(TimeSpan.Zero, ct) can return synchronously without observing the
            // token, so check explicitly before waiting and re-entering the loop.
            cancellationToken.ThrowIfCancellationRequested();
            await timeProvider.Delay(decision.Delay, cancellationToken).ConfigureAwait(false);
        }
    }
}
