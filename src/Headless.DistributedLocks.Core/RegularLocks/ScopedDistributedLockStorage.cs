// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

public sealed class ScopedDistributedLockStorage(
    IDistributedLockStorage innerDistributedLockStorage,
    string scopedPrefix
) : IDistributedLockStorage
{
    public ValueTask<bool> InsertAsync(
        string key,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        return innerDistributedLockStorage.InsertAsync(_NormalizeResource(key), lockId, ttl, cancellationToken);
    }

    public ValueTask<bool> ReplaceIfEqualAsync(
        string key,
        string expectedId,
        string newId,
        TimeSpan? newTtl = null,
        CancellationToken cancellationToken = default
    )
    {
        return innerDistributedLockStorage.ReplaceIfEqualAsync(
            _NormalizeResource(key),
            expectedId,
            newId,
            newTtl,
            cancellationToken
        );
    }

    public ValueTask<bool> RemoveIfEqualAsync(
        string key,
        string expectedId,
        CancellationToken cancellationToken = default
    )
    {
        return innerDistributedLockStorage.RemoveIfEqualAsync(_NormalizeResource(key), expectedId, cancellationToken);
    }

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        return innerDistributedLockStorage.GetExpirationAsync(_NormalizeResource(key), cancellationToken);
    }

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return innerDistributedLockStorage.ExistsAsync(_NormalizeResource(key), cancellationToken);
    }

    public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return innerDistributedLockStorage.GetAsync(_NormalizeResource(key), cancellationToken);
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        var result = await innerDistributedLockStorage.GetAllByPrefixAsync(
            _NormalizeResource(prefix),
            cancellationToken
        );

        // Strip the scope prefix from keys to return unscoped resource names
        return result.ToDictionary(kv => kv.Key[scopedPrefix.Length..], kv => kv.Value, StringComparer.Ordinal);
    }

    public ValueTask<long> GetCountAsync(string resourcePrefix = "", CancellationToken cancellationToken = default)
    {
        return innerDistributedLockStorage.GetCountAsync(_NormalizeResource(resourcePrefix), cancellationToken);
    }

    private string _NormalizeResource(string resource)
    {
        return scopedPrefix + resource;
    }
}
