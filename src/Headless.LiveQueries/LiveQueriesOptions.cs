// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.LiveQueries;

/// <summary>Settings of the in-process live-query hub.</summary>
[PublicAPI]
public sealed class LiveQueriesOptions
{
    /// <summary>
    /// Gets or sets how many dependency keys the hub remembers the last change of. The hub uses that memory to
    /// re-run a query when a change was announced while the query was running. Past the limit it forgets the keys
    /// changed longest ago, and a running query that depends on a forgotten key re-runs once more to be safe, so a
    /// low limit costs extra runs and never a missed change. Default: 100,000.
    /// </summary>
    public int MaxTrackedKeys { get; set; } = 100_000;
}

internal sealed class LiveQueriesOptionsValidator : AbstractValidator<LiveQueriesOptions>
{
    public LiveQueriesOptionsValidator()
    {
        RuleFor(x => x.MaxTrackedKeys).GreaterThan(0);
    }
}
