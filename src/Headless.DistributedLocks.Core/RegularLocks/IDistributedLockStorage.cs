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
