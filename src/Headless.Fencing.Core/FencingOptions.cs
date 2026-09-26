// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Fencing;

/// <summary>The bounds every grant and renewal duration must fall within, and when a takeover is worth a warning.</summary>
[PublicAPI]
public sealed class FencingOptions
{
    /// <summary>
    /// Gets or sets the shortest duration a grant or renewal accepts. A lease shorter than a database round trip
    /// expires before its holder can use it. Default: 1 second.
    /// </summary>
    public TimeSpan MinimumLeaseDuration { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the longest duration a grant or renewal accepts. It bounds how long a crashed holder blocks the
    /// resource before a takeover or sweep can recover it. Default: 1 day.
    /// </summary>
    public TimeSpan MaximumLeaseDuration { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Gets or sets the takeover count at which a takeover grant or a sweep's abandonment logs a warning, or
    /// <see langword="null" /> to log none. Every takeover at or above the threshold logs, so a lease that keeps
    /// failing keeps being reported until an attempt settles or releases it. Must be positive when set. Default:
    /// <see langword="null" />.
    /// </summary>
    /// <remarks>
    /// Off by default because what counts as too many depends on the work: a lease over flaky, restartable work may
    /// expect a few takeovers, while one over a short job should see none.
    /// </remarks>
    public int? TakeoverWarningThreshold { get; set; }
}

internal sealed class FencingOptionsValidator : AbstractValidator<FencingOptions>
{
    public FencingOptionsValidator()
    {
        RuleFor(x => x.MinimumLeaseDuration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.MaximumLeaseDuration).GreaterThanOrEqualTo(x => x.MinimumLeaseDuration);
        RuleFor(x => x.TakeoverWarningThreshold).GreaterThan(0).When(x => x.TakeoverWarningThreshold.HasValue);
    }
}
