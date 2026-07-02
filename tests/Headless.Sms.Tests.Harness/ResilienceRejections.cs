// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace Tests;

/// <summary>
/// Builds the standard resilience pipeline's rejection exceptions from their <see langword="nameof"/> keys.
/// Theories pass the key (a serializable string) instead of the exception instance so xUnit can enumerate
/// individual data rows (exceptions are not xUnit-serializable).
/// </summary>
public static class ResilienceRejections
{
    public static Exception Create(string rejectionKind)
    {
        return rejectionKind switch
        {
            nameof(TimeoutRejectedException) => new TimeoutRejectedException("pipeline timeout"),
            nameof(BrokenCircuitException) => new BrokenCircuitException("circuit open"),
            nameof(RateLimiterRejectedException) => new RateLimiterRejectedException("rate limiter rejected"),
            _ => throw new ArgumentOutOfRangeException(nameof(rejectionKind), rejectionKind, message: null),
        };
    }
}
