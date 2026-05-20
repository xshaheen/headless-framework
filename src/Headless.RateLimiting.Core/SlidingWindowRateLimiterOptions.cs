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

    public int MaxHitsPerPeriod { get; set; } = 100;

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
