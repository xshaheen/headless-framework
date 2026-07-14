// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// An <see cref="IDistributedLockStorage"/> decorator that prepends a fixed key prefix to every
/// storage operation, scoping all lock keys under a logical namespace.
/// </summary>
/// <remarks>
/// Used by <see cref="DistributedLock"/> to apply <see cref="DistributedLockOptions.KeyPrefix"/>
/// (default <c>"distributed-lock:"</c>) without coupling the core acquire/release logic to
/// key-construction details. Prefix-scanning methods (<see cref="GetAllByPrefixAsync"/>,
/// <see cref="GetAllWithExpirationByPrefixAsync"/>, <see cref="GetCountAsync"/>) automatically
/// strip the scope prefix from returned keys so callers always see bare resource names.
/// </remarks>
internal sealed class ScopedDistributedLockStorage : IDistributedLockStorage
{
    private readonly IDistributedLockStorage _inner;
    private readonly string _scopedPrefix;

    /// <summary>
    /// Creates a new scoped storage wrapper.
    /// </summary>
    /// <param name="inner">The underlying storage backend to delegate all operations to.</param>
    /// <param name="scopedPrefix">
    /// The non-empty prefix prepended to every key before delegating to <paramref name="inner"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scopedPrefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="scopedPrefix"/> is empty.</exception>
    public ScopedDistributedLockStorage(IDistributedLockStorage inner, string scopedPrefix)
    {
        Argument.IsNotNull(inner);
        Argument.IsNotNullOrEmpty(scopedPrefix);
        _inner = inner;
        _scopedPrefix = scopedPrefix;
    }

    /// <inheritdoc/>
    public ValueTask<DistributedLockAcquireResult> InsertAsync(
        string key,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.InsertAsync(_NormalizeResource(key), leaseId, ttl, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<bool> ReplaceIfEqualAsync(
        string key,
        string expectedId,
        string newId,
        TimeSpan? newTtl = null,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.ReplaceIfEqualAsync(_NormalizeResource(key), expectedId, newId, newTtl, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<bool> RemoveIfEqualAsync(
        string key,
        string expectedId,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.RemoveIfEqualAsync(_NormalizeResource(key), expectedId, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        return _inner.GetExpirationAsync(_NormalizeResource(key), cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return _inner.ExistsAsync(_NormalizeResource(key), cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return _inner.GetAsync(_NormalizeResource(key), cancellationToken);
    }

    /// <inheritdoc cref="IDistributedLockStorage.GetAllByPrefixAsync"/>
    /// <remarks>The scope prefix is stripped from returned keys so callers receive bare resource names.</remarks>
    public async ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        var result = await _inner
            .GetAllByPrefixAsync(_NormalizeResource(prefix), cancellationToken)
            .ConfigureAwait(false);

        // Strip the scope prefix from keys to return unscoped resource names
        return result.ToDictionary(kv => kv.Key[_scopedPrefix.Length..], kv => kv.Value, StringComparer.Ordinal);
    }

    /// <inheritdoc cref="IDistributedLockStorage.GetAllWithExpirationByPrefixAsync"/>
    /// <remarks>The scope prefix is stripped from returned keys so callers receive bare resource names.</remarks>
    public async ValueTask<
        IReadOnlyDictionary<string, (string LeaseId, TimeSpan? Ttl)>
    > GetAllWithExpirationByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var result = await _inner
            .GetAllWithExpirationByPrefixAsync(_NormalizeResource(prefix), cancellationToken)
            .ConfigureAwait(false);

        // Strip the scope prefix from keys to return unscoped resource names
        return result.ToDictionary(kv => kv.Key[_scopedPrefix.Length..], kv => kv.Value, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        return _inner.GetCountAsync(_NormalizeResource(prefix), cancellationToken);
    }

    private string _NormalizeResource(string resource)
    {
        return _scopedPrefix + resource;
    }
}
