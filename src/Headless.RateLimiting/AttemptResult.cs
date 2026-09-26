// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Exceptions;
using Headless.Primitives;

namespace Headless.RateLimiting;

/// <summary>The outcome of charging one attempt against a subject's budget.</summary>
/// <remarks>
/// A result names the exact window it was charged to rather than describing it, so
/// <see cref="IAttemptLimiter.ResetAsync"/> clears that budget even when the window has since rolled over.
/// It holds no resource and needs no disposal. Construct one directly to stub <see cref="IAttemptLimiter"/> in tests,
/// or to return results from a custom limiter.
/// </remarks>
[PublicAPI]
public sealed class AttemptResult
{
    /// <summary>Initializes a result.</summary>
    /// <param name="purpose">The purpose the attempt was charged to.</param>
    /// <param name="count">The attempts charged in the window, including this one; at least one.</param>
    /// <param name="limit">The limit in force; at least one.</param>
    /// <param name="retryAfter">How long until the window closes; positive.</param>
    /// <param name="resetToken">
    /// The limiter-defined handle <see cref="IAttemptLimiter.ResetAsync"/> needs to find this budget again, or
    /// <see langword="null"/> for a result that cannot be reset, such as a test stub.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="purpose"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="purpose"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="count"/>, <paramref name="limit"/>, or <paramref name="retryAfter"/> is not positive.
    /// </exception>
    public AttemptResult(string purpose, long count, int limit, TimeSpan retryAfter, string? resetToken = null)
    {
        Argument.IsNotNullOrEmpty(purpose);
        Argument.IsPositive(count);
        Argument.IsPositive(limit);
        Argument.IsPositive(retryAfter);

        Purpose = purpose;
        Count = count;
        Limit = limit;
        RetryAfter = retryAfter;
        ResetToken = resetToken;
    }

    /// <summary>
    /// Opaque, limiter-defined handle that <see cref="IAttemptLimiter.ResetAsync"/> uses to clear the exact budget
    /// this attempt was charged to. Its format belongs to the limiter that issued it; do not parse or persist it.
    /// <see langword="null"/> when the result cannot be reset.
    /// </summary>
    public string? ResetToken { get; }

    /// <summary>The purpose the attempt was charged to.</summary>
    public string Purpose { get; }

    /// <summary>
    /// The attempts charged in the current window, including this one and any that were refused.
    /// </summary>
    public long Count { get; }

    /// <summary>The limit that was in force when this attempt was charged.</summary>
    public int Limit { get; }

    /// <summary>Whether the attempt fits within the budget and the guarded work may proceed.</summary>
    public bool IsAllowed => Count <= Limit;

    /// <summary>The attempts still admitted in the current window after this one.</summary>
    public long Remaining => Math.Max(0, Limit - Count);

    /// <summary>
    /// How long until the current window closes and the budget reopens; the built-in limiter reports at least one
    /// second. It is the wait a
    /// caller needs only once <see cref="Remaining"/> reaches zero; a fixed window imposes no spacing before that.
    /// </summary>
    public TimeSpan RetryAfter { get; }

    /// <summary>Throws when the attempt was refused; does nothing otherwise.</summary>
    /// <param name="error">
    /// Optional descriptor naming the exhausted budget, carried into the 429 response by the API exception handler.
    /// </param>
    /// <exception cref="TooManyRequestsException">The attempt exceeded the budget.</exception>
    public void ThrowIfRejected(ErrorDescriptor? error = null)
    {
        if (!IsAllowed)
        {
            throw new TooManyRequestsException(RetryAfter, error);
        }
    }
}
