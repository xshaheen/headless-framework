// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.RateLimiting;

[PublicAPI]
public sealed class SlidingWindowRateLimiterOptions
{
    /// <summary>Resource rate-limiter key prefix.</summary>
    public string KeyPrefix { get; set; } = "rate-limiter:";

    /// <summary>
    /// Maximum number of leases that may be acquired per <see cref="RateLimitingPeriod"/>.
    /// This bound is <b>inclusive</b>: when the running hit count is less than or equal to this value,
    /// the lease is granted; the (N+1)th acquisition within the period is denied.
    /// </summary>
    public int MaxHitsPerPeriod { get; set; } = 100;

    /// <summary>
    /// Length of the sliding rate-limiting window. Lease counters are bucketed by period start, and
    /// callers that fail to acquire a lease will wait at most one period rotation before retrying.
    /// </summary>
    public TimeSpan RateLimitingPeriod { get; set; } = TimeSpan.FromMinutes(15);
}

internal sealed class SlidingWindowRateLimiterOptionsValidator : AbstractValidator<SlidingWindowRateLimiterOptions>
{
    public SlidingWindowRateLimiterOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotEmpty();
        RuleFor(x => x.MaxHitsPerPeriod).GreaterThan(0);
        RuleFor(x => x.RateLimitingPeriod).GreaterThan(TimeSpan.Zero);
    }
}
