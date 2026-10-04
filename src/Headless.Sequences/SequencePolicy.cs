// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences;

/// <summary>Defines how one counter name numbers: its start value, its step, and the entry point that serves it.</summary>
/// <remarks>
/// <see cref="Start" /> is read only when a key's row is created, so changing it never moves an existing counter.
/// A changed <see cref="Step" /> applies from the next call, and the existing counter then mixes both steps.
/// </remarks>
[PublicAPI]
public sealed record SequencePolicy
{
    /// <summary>Gets the initial value allocated by a new counter. The default is 1.</summary>
    public long Start { get; init; } = 1;

    /// <summary>Gets the distance between two consecutive values; greater than 0. The default is 1.</summary>
    public long Step { get; init; } = 1;

    /// <summary>
    /// Gets the entry point that serves the counter: <see cref="SequenceMode.Fast" /> through
    /// <see cref="ISequenceGenerator" />, or <see cref="SequenceMode.GapFree" /> through <c>unit.Sequences</c>.
    /// The default is <see cref="SequenceMode.Fast" />.
    /// </summary>
    public SequenceMode Mode { get; init; } = SequenceMode.Fast;
}
