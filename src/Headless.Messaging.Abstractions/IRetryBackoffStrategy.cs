// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Defines a strategy that classifies a failed delivery attempt and computes the next retry delay.
/// </summary>
[PublicAPI]
public interface IRetryBackoffStrategy
{
    /// <summary>
    /// Decides what to do after a failure. Strategies fold permanent-vs-transient classification and
    /// delay computation into a single call, returning <see cref="RetryDecision.Stop"/> for
    /// non-retryable exceptions or <see cref="RetryDecision.Continue"/> with the delay otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Typical strategies return only <see cref="RetryDecision.Stop"/> or
    /// <see cref="RetryDecision.Continue"/>; the framework emits <see cref="RetryDecision.Exhausted"/>
    /// on its behalf when the configured <c>MaxInlineRetries</c>/<c>MaxPersistedRetries</c> budgets
    /// are consumed.
    /// </para>
    /// <para>
    /// Strategies that implement their own attempt accounting (for example, a custom budget keyed
    /// on the exception type) MAY return <see cref="RetryDecision.Exhausted"/> directly. The retry
    /// pipeline treats both sources identically: the persisted-retry counter is preserved, the row
    /// transitions to terminal <c>Failed</c>, and the configured <c>OnExhausted</c> callback fires.
    /// </para>
    /// </remarks>
    /// <param name="retryCount">The number of retries already performed for this message (0-based).</param>
    /// <param name="exception">The exception that caused the failure.</param>
    RetryDecision Compute(int retryCount, Exception exception);
}
