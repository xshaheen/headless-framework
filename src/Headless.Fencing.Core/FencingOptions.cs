// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Fencing;

/// <summary>The bounds every grant and renewal duration must fall within.</summary>
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
}

internal sealed class FencingOptionsValidator : AbstractValidator<FencingOptions>
{
    public FencingOptionsValidator()
    {
        RuleFor(x => x.MinimumLeaseDuration).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.MaximumLeaseDuration).GreaterThanOrEqualTo(x => x.MinimumLeaseDuration);
    }
}
