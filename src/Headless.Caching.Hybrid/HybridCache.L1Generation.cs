// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

public sealed partial class HybridCache
{
    // A read that misses L1 fetches from L2 and then fills L1. A write or an invalidation that lands between the fetch
    // and the fill must win, or the fill puts the older L2 value back and this node serves it until the local entry
    // expires. Every L1 mutation therefore bumps a generation first, and a fill commits only while the generation it
    // captured before its L2 fetch still holds. The counters are a fixed array of stripes indexed by key hash, so the
    // guard uses bounded memory; two keys sharing a stripe only cost a skipped fill, never a stale one.
    private const int _L1GenerationStripes = 4096;

    private readonly long[] _l1KeyGenerations = new long[_L1GenerationStripes];
    private long _l1GlobalGeneration;

    /// <summary>Reads the generation a later fill of <paramref name="key"/> must still match.</summary>
    private (long Global, long Key) _CaptureL1Generation(string key)
    {
        return (Volatile.Read(ref _l1GlobalGeneration), Volatile.Read(ref _l1KeyGenerations[_L1Stripe(key)]));
    }

    private bool _L1GenerationHolds(string key, (long Global, long Key) captured)
    {
        return Volatile.Read(ref _l1GlobalGeneration) == captured.Global
            && Volatile.Read(ref _l1KeyGenerations[_L1Stripe(key)]) == captured.Key;
    }

    /// <summary>Marks <paramref name="key"/> changed and returns the local cache to mutate it in.</summary>
    private IInMemoryCache _L1For(string key)
    {
        Interlocked.Increment(ref _l1KeyGenerations[_L1Stripe(key)]);

        return LocalCache;
    }

    /// <summary>Marks every key changed (a multi-key, prefix, tag, clear, or flush mutation) and returns the local cache.</summary>
    private IInMemoryCache _L1ForAll()
    {
        Interlocked.Increment(ref _l1GlobalGeneration);

        return LocalCache;
    }

    private static int _L1Stripe(string key)
    {
        return (int)((uint)StringComparer.Ordinal.GetHashCode(key) % _L1GenerationStripes);
    }

    /// <summary>
    /// Fills L1 with an entry read from L2 unless a mutation of <paramref name="key"/> happened since
    /// <paramref name="captured"/>. A mutation that lands during the fill itself is caught by the second check, which
    /// drops the just-written entry so the next read refills from L2.
    /// </summary>
    private async ValueTask _PromoteReadToLocalAsync<T>(
        IFactoryCacheStore l1Store,
        string key,
        CacheStoreEntry<T> entry,
        (long Global, long Key) captured,
        CancellationToken cancellationToken
    )
    {
        if (!_L1GenerationHolds(key, captured))
        {
            return;
        }

        await _SetLocalEntryAsync(l1Store, key, entry, expectedConcurrencyStamp: null, cancellationToken)
            .ConfigureAwait(false);

        await _DropFillIfMutatedAsync(key, captured).ConfigureAwait(false);
    }

    /// <summary>Removes a fill of <paramref name="key"/> that a concurrent mutation overtook.</summary>
    private async ValueTask _DropFillIfMutatedAsync(string key, (long Global, long Key) captured)
    {
        if (!_L1GenerationHolds(key, captured))
        {
            // Not cancellable: a fill left in place after the caller gave up would be the stale value this guards.
            await LocalCache.RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private Dictionary<string, (long Global, long Key)> _CaptureL1Generations(IReadOnlyCollection<string> keys)
    {
        var generations = new Dictionary<string, (long Global, long Key)>(keys.Count, StringComparer.Ordinal);

        foreach (var key in keys)
        {
            generations[key] = _CaptureL1Generation(key);
        }

        return generations;
    }

    /// <summary>The plain-value counterpart of <see cref="_PromoteReadToLocalAsync{T}"/> for an L2 that has no entry framing.</summary>
    private async ValueTask _PromoteValueToLocalAsync<T>(
        string key,
        T value,
        TimeSpan? localExpiration,
        (long Global, long Key) captured,
        CancellationToken cancellationToken
    )
    {
        if (!_L1GenerationHolds(key, captured))
        {
            return;
        }

        await LocalCache.UpsertAsync(key, value, localExpiration, cancellationToken).ConfigureAwait(false);
        await _DropFillIfMutatedAsync(key, captured).ConfigureAwait(false);
    }
}
