// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal sealed class ScopedDistributedReadWriteLockStorage : IDistributedReadWriteLockStorage
{
    private readonly IDistributedReadWriteLockStorage _inner;
    private readonly string _scopedPrefix;

    public ScopedDistributedReadWriteLockStorage(IDistributedReadWriteLockStorage inner, string scopedPrefix)
    {
        _inner = Argument.IsNotNull(inner);
        _scopedPrefix = Argument.IsNotNullOrEmpty(scopedPrefix);
    }

    public ValueTask<bool> TryAcquireReadAsync(
        string resource,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.TryAcquireReadAsync(_NormalizeResource(resource), leaseId, ttl, cancellationToken);
    }

    public ValueTask<bool> TryExtendReadAsync(
        string resource,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.TryExtendReadAsync(_NormalizeResource(resource), leaseId, ttl, cancellationToken);
    }

    public ValueTask ReleaseReadAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        return _inner.ReleaseReadAsync(_NormalizeResource(resource), leaseId, cancellationToken);
    }

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

    public ValueTask<bool> TryExtendWriteAsync(
        string resource,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.TryExtendWriteAsync(_NormalizeResource(resource), leaseId, ttl, cancellationToken);
    }

    public ValueTask ReleaseWriteAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        return _inner.ReleaseWriteAsync(_NormalizeResource(resource), leaseId, cancellationToken);
    }

    public ValueTask<bool> ValidateReadAsync(
        string resource,
        string leaseId,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.ValidateReadAsync(_NormalizeResource(resource), leaseId, cancellationToken);
    }

    public ValueTask<bool> ValidateWriteAsync(
        string resource,
        string leaseId,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.ValidateWriteAsync(_NormalizeResource(resource), leaseId, cancellationToken);
    }

    public ValueTask<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        return _inner.IsReadLockedAsync(_NormalizeResource(resource), cancellationToken);
    }

    public ValueTask<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        return _inner.IsWriteLockedAsync(_NormalizeResource(resource), cancellationToken);
    }

    public ValueTask<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        return _inner.GetReaderCountAsync(_NormalizeResource(resource), cancellationToken);
    }

    private string _NormalizeResource(string resource)
    {
        return _scopedPrefix + resource;
    }
}
