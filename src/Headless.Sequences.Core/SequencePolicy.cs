// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences;

/// <summary>Defines configuration policy for a counter name, specifying start value, step increment, and allocation mode.</summary>
/// <remarks>
/// <see cref="Start" /> applies only when a counter row is initially created.
/// Updates to <see cref="Step" /> take effect on subsequent allocations.
/// </remarks>
[PublicAPI]
public sealed record SequencePolicy
{
    /// <summary>Gets the initial value allocated by a new counter. The default is 1.</summary>
    public long Start { get; init; } = 1;

    /// <summary>Gets the step interval between consecutive values. Must be greater than 0. The default is 1.</summary>
    public long Step { get; init; } = 1;

    /// <summary>
    /// Gets the allocation mode for the counter: <see cref="SequenceMode.Fast" /> through
    /// <see cref="ISequenceGenerator" />, or <see cref="SequenceMode.GapFree" /> through <c>unit.Sequences</c>.
    /// The default is <see cref="SequenceMode.Fast" />.
    /// </summary>
    public SequenceMode Mode { get; init; } = SequenceMode.Fast;
}
