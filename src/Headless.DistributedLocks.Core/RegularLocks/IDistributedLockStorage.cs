// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

public interface IDistributedLockStorage
{
    ValueTask<bool> InsertAsync(
        string key,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> ReplaceIfEqualAsync(
        string key,
        string expectedId,
        string newId,
        TimeSpan? newTtl = null,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> RemoveIfEqualAsync(string key, string expectedId, CancellationToken cancellationToken = default);

    ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default);

    ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Gets the lock ID stored for the given key, or null if not found.</summary>
    ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Gets all lock keys and their IDs matching the given prefix.</summary>
    ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    );

    /// <summary>Gets the count of locks matching the given prefix.</summary>
    ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default);
}

public sealed class ScopedDistributedLockStorage(IDistributedLockStorage innerStorage, string prefix)
    : IDistributedLockStorage
{
    public ValueTask<bool> InsertAsync(
        string key,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    ) => innerStorage.InsertAsync(_NormalizeResource(key), lockId, ttl, cancellationToken);

    public ValueTask<bool> ReplaceIfEqualAsync(
        string key,
        string expectedId,
        string newId,
        TimeSpan? newTtl = null,
        CancellationToken cancellationToken = default
    ) => innerStorage.ReplaceIfEqualAsync(_NormalizeResource(key), expectedId, newId, newTtl, cancellationToken);

    public ValueTask<bool> RemoveIfEqualAsync(
        string key,
        string expectedId,
        CancellationToken cancellationToken = default
    ) => innerStorage.RemoveIfEqualAsync(_NormalizeResource(key), expectedId, cancellationToken);

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default) =>
        innerStorage.GetExpirationAsync(_NormalizeResource(key), cancellationToken);

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        innerStorage.ExistsAsync(_NormalizeResource(key), cancellationToken);

    public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        innerStorage.GetAsync(_NormalizeResource(key), cancellationToken);

    public async ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(
        string resourcePrefix,
        CancellationToken cancellationToken = default
    )
    {
        var result = await innerStorage.GetAllByPrefixAsync(_NormalizeResource(resourcePrefix), cancellationToken);

        // Strip the scope prefix from keys to return unscoped resource names
        return result.ToDictionary(kv => kv.Key[prefix.Length..], kv => kv.Value, StringComparer.Ordinal);
    }

    public ValueTask<long> GetCountAsync(string resourcePrefix = "", CancellationToken cancellationToken = default) =>
        innerStorage.GetCountAsync(_NormalizeResource(resourcePrefix), cancellationToken);

    private string _NormalizeResource(string resource) => prefix + resource;
}
