// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Decorates an <see cref="IDistributedReadWriteLockStorage"/> with a key prefix so that all
/// resource names are namespaced by <see cref="DistributedLockOptions.KeyPrefix"/>. This avoids
/// collisions between distributed lock keys and other keys in the same storage backend
/// (e.g., a shared Redis instance). All <see cref="IDistributedReadWriteLockStorage"/> method
/// calls are forwarded to the inner storage with the prefix prepended to
/// <c>resource</c>.
/// </summary>
/// <remarks>
/// Initializes the scoped wrapper.
/// </remarks>
/// <param name="inner">The real storage backend to delegate all calls to.</param>
/// <param name="scopedPrefix">
/// Non-empty prefix prepended to every resource key before forwarding to
/// <paramref name="inner"/>. Typically sourced from
/// <see cref="DistributedLockOptions.KeyPrefix"/>.
/// </param>
/// <exception cref="ArgumentNullException">
/// Thrown when <paramref name="inner"/> is <see langword="null"/>.
/// </exception>
/// <exception cref="ArgumentException">
/// Thrown when <paramref name="scopedPrefix"/> is <see langword="null"/> or empty.
/// </exception>
internal sealed class ScopedDistributedReadWriteLockStorage(IDistributedReadWriteLockStorage inner, string scopedPrefix)
    : IDistributedReadWriteLockStorage
{
    private readonly IDistributedReadWriteLockStorage _inner = Argument.IsNotNull(inner);
    private readonly string _scopedPrefix = Argument.IsNotNullOrEmpty(scopedPrefix);

    /// <inheritdoc/>
    public ValueTask<bool> TryAcquireReadAsync(
        string resource,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.TryAcquireReadAsync(_NormalizeResource(resource), leaseId, ttl, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<bool> TryExtendReadAsync(
        string resource,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.TryExtendReadAsync(_NormalizeResource(resource), leaseId, ttl, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask ReleaseReadAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        return _inner.ReleaseReadAsync(_NormalizeResource(resource), leaseId, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<bool> TryAcquireWriteAsync(
        string resource,
        string leaseId,
        string waitingId,
        TimeSpan? ttl = null,
        TimeSpan? markerTtl = null,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.TryAcquireWriteAsync(
            _NormalizeResource(resource),
            leaseId,
            waitingId,
            ttl,
            markerTtl,
            cancellationToken
        );
    }

    /// <inheritdoc/>
    public ValueTask<bool> TryExtendWriteAsync(
        string resource,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.TryExtendWriteAsync(_NormalizeResource(resource), leaseId, ttl, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask ReleaseWriteAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        return _inner.ReleaseWriteAsync(_NormalizeResource(resource), leaseId, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<bool> ValidateReadAsync(
        string resource,
        string leaseId,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.ValidateReadAsync(_NormalizeResource(resource), leaseId, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<bool> ValidateWriteAsync(
        string resource,
        string leaseId,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.ValidateWriteAsync(_NormalizeResource(resource), leaseId, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        return _inner.IsReadLockedAsync(_NormalizeResource(resource), cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        return _inner.IsWriteLockedAsync(_NormalizeResource(resource), cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        return _inner.GetReaderCountAsync(_NormalizeResource(resource), cancellationToken);
    }

    private string _NormalizeResource(string resource)
    {
        return _scopedPrefix + resource;
    }
}
