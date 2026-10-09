// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Headless.Messaging.Redis;

/// <summary>
/// Hands out the consumer names a process reads a consumer group under: <c>{group}:{machine}:{slot}</c>, with the lowest
/// slot no live client of that group holds.
/// </summary>
/// <remarks>
/// A name must outlive the process for the startup read of the consumer's own pending entries to find anything: a
/// process restarted on the same machine reads its group under the names it used before and resumes the entries it
/// left pending at once, instead of waiting for another consumer to claim them. A name must also be unique among the
/// live clients of one process, so the clients a subscription runs in parallel each take their own slot.
/// </remarks>
internal sealed class RedisConsumerNames(string machineName)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, HashSet<int>> _taken = new(StringComparer.Ordinal);

    public RedisConsumerNames()
        : this(Environment.MachineName) { }

    public RedisConsumerNameLease Acquire(string consumerGroup)
    {
        lock (_lock)
        {
            if (!_taken.TryGetValue(consumerGroup, out var slots))
            {
                slots = [];
                _taken[consumerGroup] = slots;
            }

            var slot = 0;
            while (slots.Contains(slot))
            {
                slot++;
            }

            slots.Add(slot);

            return new RedisConsumerNameLease(
                string.Create(CultureInfo.InvariantCulture, $"{consumerGroup}:{machineName}:{slot}"),
                () => _Release(consumerGroup, slot)
            );
        }
    }

    private void _Release(string consumerGroup, int slot)
    {
        lock (_lock)
        {
            if (_taken.TryGetValue(consumerGroup, out var slots) && slots.Remove(slot) && slots.Count == 0)
            {
                _taken.Remove(consumerGroup);
            }
        }
    }
}

/// <summary>A consumer name a client holds until it shuts down; <see cref="Release"/> is idempotent.</summary>
internal sealed class RedisConsumerNameLease(string name, Action release)
{
    private int _released;

    public string Name { get; } = name;

    public void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            release();
        }
    }
}
