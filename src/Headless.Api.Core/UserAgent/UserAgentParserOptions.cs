// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Abstractions;

namespace Headless.Api.UserAgent;

/// <summary>Options for <see cref="IUserAgentParser"/>'s in-process memoization.</summary>
[PublicAPI]
public sealed class UserAgentParserOptions
{
    /// <summary>
    /// Maximum number of distinct User-Agent strings held in the parser's memo. Default <c>1000</c>,
    /// which covers a typical fleet of distinct agents without unbounded growth.
    /// </summary>
    public int MaxEntries { get; set; } = 1000;

    /// <summary>
    /// Sliding lifetime of a memoized entry. Default 6 hours. Sliding rather than absolute so rare or
    /// rotated agents fall out while the working set stays hot, with no periodic clear to stampede on.
    /// </summary>
    public TimeSpan SlidingExpiration { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// User-Agent values longer than this are truncated before parsing. Default <c>512</c>, generous for
    /// real-world agents; anything longer is likely abuse, and the parser scans the whole string.
    /// </summary>
    public int MaxUserAgentLength { get; set; } = 512;
}

internal sealed class UserAgentParserOptionsValidator : AbstractValidator<UserAgentParserOptions>
{
    public UserAgentParserOptionsValidator()
    {
        RuleFor(x => x.MaxEntries).GreaterThan(0);
        RuleFor(x => x.SlidingExpiration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.MaxUserAgentLength).GreaterThan(0);
    }
}
