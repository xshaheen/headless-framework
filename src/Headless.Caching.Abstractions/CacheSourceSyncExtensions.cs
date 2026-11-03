// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Caching;

/// <summary>Provides <see cref="ICache{T}"/> extension methods that keep cache entries in step with their source.</summary>
[PublicAPI]
public static class CacheSourceSyncExtensions
{
    /// <summary>How many compare-and-swap rounds run before the keys still contended are removed instead.</summary>
    private const int _MaxAttempts = 4;

    /// <summary>
    /// Brings the entry of each of <paramref name="keys"/> to the value its source holds now. Call it after a write to
    /// the source commits, instead of writing the value the caller wrote.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two writers that each store the value they wrote can land their cache writes in the opposite order to their
    /// commits, and the cache then keeps the losing value until it expires. This method instead reads each entry, then
    /// reads the source, then swaps the entry only if it still holds what was read (<c>TryReplaceIfEqualAsync</c>, or
    /// <c>TryInsertAsync</c> for an absent entry). A swap that fails because another write landed in between is retried
    /// with fresh reads. Whichever caller commits last reads the source after its own commit, and any later swap must
    /// have read the entry after that caller's swap, so the entry ends at the source's final value.
    /// </para>
    /// <para>
    /// Every value <paramref name="readSource"/> returns must be a new value that no other write stores, for example
    /// one carrying a fresh random stamp, and <typeparamref name="T"/> must compare by value. Otherwise a slow caller's
    /// swap can match an equal value that a newer write stored and overwrite it with an older read.
    /// </para>
    /// <para>
    /// After four contended rounds, the keys still contended are removed, so the next read loads them from the source.
    /// A key that <paramref name="readSource"/> omits is removed as well.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="cache">The cache.</param>
    /// <param name="keys">The cache keys to bring up to date.</param>
    /// <param name="readSource">
    /// Reads the current source value of each requested key. It runs once per round with the keys still pending.
    /// </param>
    /// <param name="expiration">The expiration of every entry written.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cache"/>, <paramref name="keys"/>, or <paramref name="readSource"/> is <see langword="null"/>.
    /// </exception>
    public static async ValueTask SyncWithSourceAsync<T>(
        this ICache<T> cache,
        IReadOnlyCollection<string> keys,
        Func<IReadOnlyCollection<string>, CancellationToken, ValueTask<IReadOnlyDictionary<string, T>>> readSource,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(cache);
        Argument.IsNotNull(keys);
        Argument.IsNotNull(readSource);

        var pending = keys.Distinct(StringComparer.Ordinal).ToList();

        for (var attempt = 1; pending.Count > 0 && attempt <= _MaxAttempts; attempt++)
        {
            // The entry is read before the source: a swap that succeeds proves nothing wrote the entry since, so every
            // write it overtakes committed before this source read.
            var current = await cache.GetAllAsync(pending, cancellationToken).ConfigureAwait(false);
            var fresh = await readSource(pending, cancellationToken).ConfigureAwait(false);

            var keysInRound = new List<string>(pending.Count);
            var swaps = new List<Task<bool>>(pending.Count);
            var omitted = new List<string>();

            foreach (var key in pending)
            {
                if (!fresh.TryGetValue(key, out var value))
                {
                    omitted.Add(key);

                    continue;
                }

                keysInRound.Add(key);
                swaps.Add(
                    current.TryGetValue(key, out var entry) && entry.HasValue
                        ? cache.TryReplaceIfEqualAsync(key, entry.Value, value, expiration, cancellationToken).AsTask()
                        : cache.TryInsertAsync(key, value, expiration, cancellationToken).AsTask()
                );
            }

            if (omitted.Count > 0)
            {
                await cache.RemoveAllAsync(omitted, cancellationToken).ConfigureAwait(false);
            }

            var results = await Task.WhenAll(swaps).ConfigureAwait(false);
            var contended = new List<string>();

            for (var i = 0; i < results.Length; i++)
            {
                if (!results[i])
                {
                    contended.Add(keysInRound[i]);
                }
            }

            pending = contended;
        }

        if (pending.Count > 0)
        {
            await cache.RemoveAllAsync(pending, cancellationToken).ConfigureAwait(false);
        }
    }
}
