// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

public sealed class ScopedDistributedReaderWriterLockStorage : IDistributedReaderWriterLockStorage
{
    private readonly IDistributedReaderWriterLockStorage _inner;
    private readonly string _scopedPrefix;

    public ScopedDistributedReaderWriterLockStorage(IDistributedReaderWriterLockStorage inner, string scopedPrefix)
    {
        _inner = Argument.IsNotNull(inner);
        _scopedPrefix = Argument.IsNotNullOrEmpty(scopedPrefix);
    }

    public ValueTask<bool> TryAcquireReadAsync(
        string resource,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.TryAcquireReadAsync(_NormalizeResource(resource), lockId, ttl, cancellationToken);
    }

    public ValueTask<bool> TryExtendReadAsync(
        string resource,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.TryExtendReadAsync(_NormalizeResource(resource), lockId, ttl, cancellationToken);
    }

    public ValueTask ReleaseReadAsync(
        string resource,
        string lockId,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.ReleaseReadAsync(_NormalizeResource(resource), lockId, cancellationToken);
    }

    public ValueTask<bool> TryAcquireWriteAsync(
        string resource,
        string lockId,
        string waitingId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.TryAcquireWriteAsync(
            _NormalizeResource(resource),
            lockId,
            waitingId,
            ttl,
            cancellationToken
        );
    }

    public ValueTask<bool> TryExtendWriteAsync(
        string resource,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.TryExtendWriteAsync(_NormalizeResource(resource), lockId, ttl, cancellationToken);
    }

    public ValueTask ReleaseWriteAsync(
        string resource,
        string lockId,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.ReleaseWriteAsync(_NormalizeResource(resource), lockId, cancellationToken);
    }

    public ValueTask<bool> ValidateReadAsync(
        string resource,
        string lockId,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.ValidateReadAsync(_NormalizeResource(resource), lockId, cancellationToken);
    }

    public ValueTask<bool> ValidateWriteAsync(
        string resource,
        string lockId,
        CancellationToken cancellationToken = default
    )
    {
        return _inner.ValidateWriteAsync(_NormalizeResource(resource), lockId, cancellationToken);
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
