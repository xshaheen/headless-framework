// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Threading;
using Microsoft.Extensions.Options;

namespace Headless.Sequences;

/// <summary>
/// Hands out fast-mode values from blocks reserved in one store call, for names whose policy sets a
/// <see cref="SequencePolicy.BlockSize" /> above 1.
/// </summary>
/// <remarks>
/// <para>
/// Each block is a range the store reserved atomically, so a value taken from one is never issued anywhere else, even
/// by another host holding its own block of the same counter. Hosts interleave: values stay unique but are no longer in
/// call order across hosts. A block belongs to one key, partition included, so it never spans a document-number reset;
/// a period's unused values are never issued once the period's partition stops being asked for.
/// </para>
/// <para>
/// At most <see cref="SequencesOptions.MaxCachedBlocks" /> keys hold a block. Adding one past the bound drops the least
/// recently used tenth; their unused values become gaps, as they do when the process stops.
/// </para>
/// </remarks>
internal sealed class SequenceBlocks(ISequenceStore store, IOptionsMonitor<SequencesOptions> options) : IDisposable
{
    private readonly ConcurrentDictionary<SequenceKey, Entry> _entries = new();

    // One caller per key reserves the next block while the others wait for it, so a hot key's exhausted block costs one
    // store call, not one per waiting caller.
    private readonly KeyedAsyncLock _refills = new();

    private readonly Lock _trimLock = new();
    private long _clock;

    /// <summary>Gets how many keys hold a block.</summary>
    internal int Count => _entries.Count;

    /// <summary>Takes the next value of <paramref name="key" /> from its block, reserving a new block when it has none left.</summary>
    public async ValueTask<long> NextAsync(SequenceKey key, SequencePolicy policy, CancellationToken cancellationToken)
    {
        var entry = _Entry(key);

        if (entry.TryTake(policy, out var value))
        {
            return value;
        }

        using var held = await _refills.LockAsync(_LockKey(key), cancellationToken).ConfigureAwait(false);

        // Another caller may have reserved a block while this one waited.
        if (entry.TryTake(policy, out value))
        {
            return value;
        }

        var range = await ReserveAsync(store, key, policy, policy.BlockSize, cancellationToken).ConfigureAwait(false);
        var block = new Block(range);
        block.TryTake(out value);
        entry.Current = block;

        return value;
    }

    /// <summary>Reserves <paramref name="count" /> consecutive values of <paramref name="key" /> in one store call.</summary>
    /// <exception cref="OverflowException">The range cannot fit in a <see cref="long" />.</exception>
    public static async ValueTask<SequenceRange> ReserveAsync(
        ISequenceStore store,
        SequenceKey key,
        SequencePolicy policy,
        int count,
        CancellationToken cancellationToken
    )
    {
        // Checked so a block that cannot fit in a long is refused before it reaches the database. A new row stores
        // the block's last value; an existing row advances by the whole block.
        var span = checked((count - 1) * policy.Step);
        var delta = checked(count * policy.Step);
        var insertValue = checked(policy.Start + span);

        var last = await store.IncrementAsync(key, insertValue, delta, cancellationToken).ConfigureAwait(false);

        return new SequenceRange(last - span, count, policy.Step);
    }

    public void Dispose() => _refills.Dispose();

    private Entry _Entry(SequenceKey key)
    {
        var stamp = Interlocked.Increment(ref _clock);

        if (_entries.TryGetValue(key, out var entry))
        {
            entry.LastUsed = stamp;

            return entry;
        }

        entry = _entries.GetOrAdd(key, static (_, stamp) => new Entry { LastUsed = stamp }, stamp);

        var max = options.CurrentValue.MaxCachedBlocks;

        if (_entries.Count > max)
        {
            _Trim(max, keep: entry);
        }

        return entry;
    }

    private void _Trim(int max, Entry keep)
    {
        lock (_trimLock)
        {
            var target = max - (max / 10);
            var excess = _entries.Count - target;

            if (excess <= 0)
            {
                return;
            }

            // A caller still holding a dropped entry finishes with its block, whose values no one else holds; the key's
            // next caller reserves a fresh block.
            foreach (
                var (key, _) in _entries
                    .Where(pair => !ReferenceEquals(pair.Value, keep))
                    .OrderBy(static pair => pair.Value.LastUsed)
                    .Take(excess)
                    .ToList()
            )
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    private static string _LockKey(SequenceKey key) => string.Concat(key.TenantId, "\0", key.Name, "\0", key.Partition);

    private sealed class Entry
    {
        private Block? _current;

        // Only orders eviction, so a stale read costs at most a slightly wrong choice of which cold key to drop.
        public long LastUsed { get; set; }

        public Block? Current
        {
            set => Volatile.Write(ref _current, value);
        }

        public bool TryTake(SequencePolicy policy, out long value)
        {
            var block = Volatile.Read(ref _current);

            // A block reserved under another step or block size is dropped: its values become a gap, and the next block
            // follows the policy now in force.
            if (block is not null && block.Matches(policy) && block.TryTake(out value))
            {
                return true;
            }

            value = 0;

            return false;
        }
    }

    private sealed class Block(SequenceRange range)
    {
        private int _taken = -1;

        public bool Matches(SequencePolicy policy) => range.Step == policy.Step && range.Count == policy.BlockSize;

        public bool TryTake(out long value)
        {
            var index = Interlocked.Increment(ref _taken);

            if (index >= range.Count)
            {
                // Pin the counter at the end so an exhausted block cannot wrap after int.MaxValue more attempts.
                Volatile.Write(ref _taken, range.Count);
                value = 0;

                return false;
            }

            value = range.First + (index * range.Step);

            return true;
        }
    }
}
