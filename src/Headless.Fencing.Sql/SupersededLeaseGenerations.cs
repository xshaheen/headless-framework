// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Fencing;

/// <summary>
/// The lease generations a status read found replaced by a later grant. Such a generation can never be current again,
/// because a grant always draws a generation above every earlier one, so a status read of it answers
/// <see cref="LeaseFenceStatus.Stale" /> without a round trip. Late work from a replaced attempt is the common caller.
/// </summary>
/// <remarks>
/// Bounded: past <see cref="Capacity" /> entries the set starts over, so memory stays flat under any number of keys and
/// a forgotten generation only costs one more read.
/// </remarks>
internal sealed class SupersededLeaseGenerations
{
    internal const int Capacity = 10_000;

    private readonly ConcurrentDictionary<(LeaseKey Key, long Generation), byte> _generations = new();

    // Counted beside the dictionary because its Count takes every lock; a race only lets the set run a few entries over.
    private int _count;

    public bool Contains(LeaseKey key, long generation) => _generations.ContainsKey((key, generation));

    public void Add(LeaseKey key, long generation)
    {
        if (!_generations.TryAdd((key, generation), 0))
        {
            return;
        }

        if (Interlocked.Increment(ref _count) > Capacity)
        {
            _generations.Clear();
            Interlocked.Exchange(ref _count, 0);
        }
    }
}
