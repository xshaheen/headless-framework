// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences;

/// <summary>Specifies how a counter allocates numbers. Each counter name declares one mode.</summary>
/// <remarks>
/// A counter cannot use both modes. Allocations in fast mode that roll back leave gaps in a gap-free
/// counter. Fast calls made while the caller transaction holds the counter row lock result in a deadlock.
/// </remarks>
[PublicAPI]
public enum SequenceMode
{
    /// <summary>
    /// Allocates numbers through <see cref="ISequenceGenerator" />, each in a dedicated transaction. Numbers
    /// are never reused, but operations that fail after reserving a number create gaps.
    /// </summary>
    Fast = 0,

    /// <summary>
    /// Allocates numbers through <c>unit.Sequences</c> inside the active unit-of-work transaction. A rollback
    /// returns the number to prevent gaps, and concurrent writers queue until the active transaction finishes.
    /// </summary>
    GapFree = 1,
}
