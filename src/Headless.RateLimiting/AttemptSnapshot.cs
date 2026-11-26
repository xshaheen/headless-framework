// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.RateLimiting;

/// <summary>
/// A read of one subject's budget in the current window, taken by <see cref="IAttemptLimiter.PeekAsync"/> without
/// charging an attempt.
/// </summary>
/// <remarks>
/// It carries the subject's fingerprint, never the subject, so it is safe to show to an operator or write to a log.
/// Construct one directly to stub <see cref="IAttemptLimiter.PeekAsync"/> in tests.
/// </remarks>
[PublicAPI]
public sealed class AttemptSnapshot
{
    /// <summary>Initializes a snapshot.</summary>
    /// <param name="purpose">The purpose the budget belongs to.</param>
    /// <param name="fingerprint">The subject's fingerprint (<see cref="AttemptSubjectFingerprinter"/>).</param>
    /// <param name="count">The attempts charged in the window so far; zero or more.</param>
    /// <param name="limit">The limit in force; at least one.</param>
    /// <param name="windowStartsAt">When the current window opened.</param>
    /// <param name="windowEndsAt">When the current window closes; after <paramref name="windowStartsAt"/>.</param>
    /// <param name="retryAfter">How long until the window closes; positive.</param>
    /// <exception cref="ArgumentException"><paramref name="purpose"/> or <paramref name="fingerprint"/> is empty.</exception>
    /// <exception cref="ArgumentNullException">A string argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="count"/> is negative, <paramref name="limit"/> or <paramref name="retryAfter"/> is not positive,
    /// or <paramref name="windowEndsAt"/> is not after <paramref name="windowStartsAt"/>.
    /// </exception>
    public AttemptSnapshot(
        string purpose,
        string fingerprint,
        long count,
        int limit,
        DateTimeOffset windowStartsAt,
        DateTimeOffset windowEndsAt,
        TimeSpan retryAfter
    )
    {
        Argument.IsNotNullOrEmpty(purpose);
        Argument.IsNotNullOrEmpty(fingerprint);
        Argument.IsPositiveOrZero(count);
        Argument.IsPositive(limit);
        Argument.IsGreaterThan(windowEndsAt, windowStartsAt);
        Argument.IsPositive(retryAfter);

        Purpose = purpose;
        Fingerprint = fingerprint;
        Count = count;
        Limit = limit;
        WindowStartsAt = windowStartsAt;
        WindowEndsAt = windowEndsAt;
        RetryAfter = retryAfter;
    }

    /// <summary>The purpose the budget belongs to.</summary>
    public string Purpose { get; }

    /// <summary>The subject's pseudonymous fingerprint; the subject itself is never kept.</summary>
    public string Fingerprint { get; }

    /// <summary>The attempts charged in the current window, including refused ones. It can exceed <see cref="Limit"/>.</summary>
    public long Count { get; }

    /// <summary>The limit of the quota the read was taken with.</summary>
    public int Limit { get; }

    /// <summary>The attempts the next callers may still make in the current window.</summary>
    public long Remaining => Math.Max(0, Limit - Count);

    /// <summary>
    /// Whether the budget is spent, so the next <see cref="IAttemptLimiter.AcquireAsync"/> in this window is refused.
    /// </summary>
    public bool IsLockedOut => Count >= Limit;

    /// <summary>When the current window opened, aligned to absolute time.</summary>
    public DateTimeOffset WindowStartsAt { get; }

    /// <summary>When the current window closes and the budget reopens.</summary>
    public DateTimeOffset WindowEndsAt { get; }

    /// <summary>How long until the current window closes; at least one second.</summary>
    public TimeSpan RetryAfter { get; }
}
