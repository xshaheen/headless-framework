// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Api.UserAgent;

/// <summary>Options for <see cref="Headless.Abstractions.IUserAgentParser"/>'s memoization in the host's <c>ICache</c>.</summary>
[PublicAPI]
public sealed class UserAgentParserOptions
{
    /// <summary>
    /// Sliding lifetime of a memoized entry: each hit extends it, so hot agents stay cached while rare or rotated
    /// ones fall out. Default 6 hours. Must not exceed <see cref="Duration"/>.
    /// </summary>
    public TimeSpan SlidingExpiration { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Absolute cap on a memoized entry regardless of how often it is read. Default 24 hours.</summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// User-Agent values longer than this are truncated before parsing and before forming the cache key. Default
    /// <c>512</c>, generous for real-world agents; anything longer is likely abuse, and the parser scans the whole
    /// string.
    /// </summary>
    public int MaxUserAgentLength { get; set; } = 512;
}

internal sealed class UserAgentParserOptionsValidator : AbstractValidator<UserAgentParserOptions>
{
    public UserAgentParserOptionsValidator()
    {
        RuleFor(x => x.SlidingExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.Duration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.SlidingExpiration)
            .LessThanOrEqualTo(x => x.Duration)
            .WithMessage("SlidingExpiration must not exceed Duration.");
        RuleFor(x => x.MaxUserAgentLength).GreaterThan(0);
    }
}
