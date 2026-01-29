// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

public interface IResourceLockStorage
{
    ValueTask<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null);

    ValueTask<bool> ReplaceIfEqualAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null);

    ValueTask<bool> RemoveIfEqualAsync(string key, string expectedId);

    ValueTask<TimeSpan?> GetExpirationAsync(string key);

    ValueTask<bool> ExistsAsync(string key);

    /// <summary>Gets the lock ID stored for the given key, or null if not found.</summary>
    ValueTask<string?> GetAsync(string key);

    /// <summary>Gets all lock keys and their IDs matching the given prefix.</summary>
    ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(string prefix);

    /// <summary>Gets the count of locks matching the given prefix.</summary>
    ValueTask<int> GetCountAsync(string prefix = "");
}

public sealed class ScopedResourceLockStorage(IResourceLockStorage innerStorage, string prefix) : IResourceLockStorage
{
    public ValueTask<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null) =>
        innerStorage.InsertAsync(_NormalizeResource(key), lockId, ttl);

    public ValueTask<bool> ReplaceIfEqualAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null) =>
        innerStorage.ReplaceIfEqualAsync(_NormalizeResource(key), expectedId, newId, newTtl);

    public ValueTask<bool> RemoveIfEqualAsync(string key, string expectedId) =>
        innerStorage.RemoveIfEqualAsync(_NormalizeResource(key), expectedId);

    public ValueTask<TimeSpan?> GetExpirationAsync(string key) =>
        innerStorage.GetExpirationAsync(_NormalizeResource(key));

    public ValueTask<bool> ExistsAsync(string key) => innerStorage.ExistsAsync(_NormalizeResource(key));

    public ValueTask<string?> GetAsync(string key) => innerStorage.GetAsync(_NormalizeResource(key));

    public async ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(string resourcePrefix)
    {
        var result = await innerStorage.GetAllByPrefixAsync(_NormalizeResource(resourcePrefix));

        // Strip the scope prefix from keys to return unscoped resource names
        return result.ToDictionary(kv => kv.Key[prefix.Length..], kv => kv.Value, StringComparer.Ordinal);
    }

    public ValueTask<int> GetCountAsync(string resourcePrefix = "") =>
        innerStorage.GetCountAsync(_NormalizeResource(resourcePrefix));

    private string _NormalizeResource(string resource) => prefix + resource;
}
