// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Headless.RateLimiting;

/// <summary>
/// Counts attempts per purpose and subject in fixed windows shared by every process that uses the same
/// <see cref="ICache"/>. Built for flows an attacker can drive before any account exists: code delivery, password
/// recovery, and code or PIN verification keyed on a phone number, email address, IP address, or card id.
/// </summary>
/// <remarks>
/// <para>
/// Every call charges the budget, including calls that are refused, so a caller who keeps guessing past the limit
/// does not win back allowance by failing. The count is exact across processes because <c>ICache.IncrementAsync</c>
/// applies the increment and returns the new total atomically.
/// </para>
/// <para>
/// The subject never reaches the cache in the clear: the key carries an HMAC of the purpose and subject under
/// <see cref="AttemptLimiterOptions.SubjectKey"/>.
/// </para>
/// <para>
/// A counter-store failure throws <see cref="AttemptLimiterUnavailableException"/> from every member, so the guarded
/// operation fails closed.
/// </para>
/// </remarks>
[PublicAPI]
public interface IAttemptLimiter
{
    /// <summary>Charges one attempt against <paramref name="subject"/>'s budget for <paramref name="purpose"/>.</summary>
    /// <param name="purpose">
    /// The flow being protected, such as <c>"otp-delivery"</c>. It appears in the cache key in the clear, so it must
    /// not carry personal data. Purposes keep separate counters even when they share a quota.
    /// </param>
    /// <param name="subject">
    /// The normalized identifier the budget belongs to. Normalize before calling: <c>"A@x.com"</c> and
    /// <c>"a@x.com"</c> are different subjects.
    /// </param>
    /// <param name="quota">
    /// The quota in force. Passed per call so a limit read from settings applies on the next attempt.
    /// </param>
    /// <param name="cancellationToken">Cancels the counter-store call.</param>
    /// <returns>The outcome; check <see cref="AttemptResult.IsAllowed"/> or call <see cref="AttemptResult.ThrowIfRejected"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="purpose"/> or <paramref name="subject"/> is empty.</exception>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    ValueTask<AttemptResult> AcquireAsync(
        string purpose,
        string subject,
        AttemptQuota quota,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Clears the budget <paramref name="attempt"/> was charged to, after the work it guarded succeeded durably.
    /// </summary>
    /// <remarks>
    /// Call this only once the state change is committed, such as after a verified code is consumed. Resetting
    /// earlier lets a caller replay a half-finished verification without limit.
    /// </remarks>
    /// <param name="attempt">A result returned by <see cref="AcquireAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the counter-store call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="attempt"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="attempt"/> has no <see cref="AttemptResult.ResetToken"/>, so it was not issued by a limiter.
    /// </exception>
    ValueTask ResetAsync(AttemptResult attempt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads <paramref name="subject"/>'s budget for <paramref name="purpose"/> in the current window without charging
    /// an attempt.
    /// </summary>
    /// <remarks>
    /// Use it to inspect a budget, such as on an operator screen or in a diagnostic; never call
    /// <see cref="AcquireAsync"/> to look, because that spends an attempt. A subject with no attempts in the window
    /// reads as a zero count, and the read creates no counter. The snapshot carries the subject's fingerprint, never
    /// the subject.
    /// </remarks>
    /// <param name="purpose">The purpose, exactly as passed to <see cref="AcquireAsync"/>.</param>
    /// <param name="subject">The normalized subject, exactly as passed to <see cref="AcquireAsync"/>.</param>
    /// <param name="quota">
    /// The quota the flow acquires with. Its window selects the counter; a different window reads a different one.
    /// </param>
    /// <param name="cancellationToken">Cancels the counter-store call.</param>
    /// <returns>The count, limit, and window bounds of the current window.</returns>
    /// <exception cref="ArgumentException"><paramref name="purpose"/> or <paramref name="subject"/> is empty.</exception>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    ValueTask<AttemptSnapshot> PeekAsync(
        string purpose,
        string subject,
        AttemptQuota quota,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Clears <paramref name="subject"/>'s budget for <paramref name="purpose"/> in the current window, for an
    /// operator who lifts a lockout.
    /// </summary>
    /// <remarks>
    /// This removes brute-force protection for the subject until the window closes. Expose it only behind operator
    /// authorization, and record who reset which purpose and fingerprint. Earlier windows have already closed and the
    /// next window starts from zero as usual, so only the current window's counter is removed. In application flows,
    /// reset the exact budget an attempt was charged to with <see cref="ResetAsync(AttemptResult, CancellationToken)"/>.
    /// </remarks>
    /// <param name="purpose">The purpose, exactly as passed to <see cref="AcquireAsync"/>.</param>
    /// <param name="subject">The normalized subject, exactly as passed to <see cref="AcquireAsync"/>.</param>
    /// <param name="quota">The quota the flow acquires with. Its window selects the counter.</param>
    /// <param name="cancellationToken">Cancels the counter-store call.</param>
    /// <returns>
    /// <see langword="true"/> when a counter was removed; <see langword="false"/> when the window had no attempts.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="purpose"/> or <paramref name="subject"/> is empty.</exception>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    ValueTask<bool> ResetAsync(
        string purpose,
        string subject,
        AttemptQuota quota,
        CancellationToken cancellationToken = default
    );
}
