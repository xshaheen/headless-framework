// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The nonces of the signed calls this node admitted, each held until its timestamp leaves the clock-skew window, so a
/// call replayed inside the window is refused.
/// </summary>
/// <remarks>
/// <para>
/// The cache is per node, which is enough: a replay has to reach the node the call was sent to, because the signature
/// covers the request target, and that node is the one that remembers the nonce. Two instances of one node behind the
/// same address do not share it.
/// </para>
/// <para>
/// Only a call whose signature matched is recorded, so filling the cache takes the key. A full cache refuses new calls
/// rather than forget a nonce still inside its window.
/// </para>
/// </remarks>
internal sealed class ForwardingNonceCache(TimeProvider time, int capacity = ForwardingNonceCache.DefaultCapacity)
{
    /// <summary>About 1,600 signed calls a second under the default 60-second skew, far above a dashboard's traffic.</summary>
    public const int DefaultCapacity = 100_000;

    // Nonce to the last unix second its timestamp is accepted.
    private readonly ConcurrentDictionary<string, long> _seen = new(StringComparer.Ordinal);
    private int _count;
    private long _sweptAt = long.MinValue;

    /// <summary>
    /// Records <paramref name="nonce"/> until <paramref name="expiresAt"/> (unix seconds). False when it is already
    /// recorded or the cache is full.
    /// </summary>
    public bool TryUse(string nonce, long expiresAt)
    {
        var now = time.GetUtcNow().ToUnixTimeSeconds();

        // At most one sweep a second, plus one whenever the cache looks full.
        if (now > Interlocked.Read(ref _sweptAt) || Volatile.Read(ref _count) >= capacity)
        {
            _Sweep(now);
        }

        if (Volatile.Read(ref _count) >= capacity || !_seen.TryAdd(nonce, expiresAt))
        {
            return false;
        }

        Interlocked.Increment(ref _count);

        return true;
    }

    private void _Sweep(long now)
    {
        Interlocked.Exchange(ref _sweptAt, now);

        foreach (var entry in _seen)
        {
            // An expired timestamp is refused before the nonce is looked at, so its entry no longer protects anything.
            if (entry.Value < now && _seen.TryRemove(entry))
            {
                Interlocked.Decrement(ref _count);
            }
        }
    }
}
