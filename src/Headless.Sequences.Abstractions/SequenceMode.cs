// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences;

/// <summary>How a counter hands out its numbers. Each counter name declares exactly one mode.</summary>
/// <remarks>
/// A counter is never used from both entry points: a fast caller that rolls back leaves a gap in a gap-free
/// counter, and a fast call made while the caller's own unit holds the counter's row lock waits on itself.
/// </remarks>
[PublicAPI]
public enum SequenceMode
{
    /// <summary>
    /// Numbers come from the injected <see cref="ISequenceGenerator" />, each in its own short transaction. A number
    /// is never handed out twice, but one taken by a caller that later fails is not returned, so gaps are possible.
    /// </summary>
    Fast = 0,

    /// <summary>
    /// Numbers come from <c>unit.Sequences</c>, inside the caller's unit-of-work transaction. A unit that rolls back
    /// returns its number, so the sequence has no gaps, and every writer of the counter waits for the one before it
    /// to commit or roll back.
    /// </summary>
    GapFree = 1,
}
