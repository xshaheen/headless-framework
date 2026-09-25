// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Exceptions;
using Headless.Primitives;

namespace Headless.RateLimiting;

/// <summary>The outcome of charging one attempt against a subject's budget.</summary>
/// <remarks>
/// A result names the exact window it was charged to rather than describing it, so
/// <see cref="IAttemptLimiter.ResetAsync"/> clears that budget even when the window has since rolled over.
/// It holds no resource and needs no disposal.
/// </remarks>
[PublicAPI]
public sealed class AttemptResult
{
    internal AttemptResult(string purpose, string cacheKey, long count, int limit, TimeSpan retryAfter)
    {
        Purpose = purpose;
        CacheKey = cacheKey;
        Count = count;
        Limit = limit;
        RetryAfter = retryAfter;
    }

    internal string CacheKey { get; }

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
    /// How long until the current window closes and the budget reopens. Always at least one second. It is the wait a
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
