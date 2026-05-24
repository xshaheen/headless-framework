// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis;

public sealed class RedisDistributedReaderWriterLockStorage(
    IConnectionMultiplexer multiplexer,
    HeadlessRedisScriptsLoader scriptsLoader
) : IDistributedReaderWriterLockStorage
{
    public const string WriterWaitingSuffix = ":_WRITERWAITING";

    private IDatabase Db => multiplexer.GetDatabase();

    public async ValueTask<bool> TryAcquireReadAsync(
        string resource,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(lockId);
        cancellationToken.ThrowIfCancellationRequested();

        return await scriptsLoader
            .TryAcquireReadLockAsync(Db, keys.WriterKey, keys.ReaderKey, lockId, ttl, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> TryExtendReadAsync(
        string resource,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(lockId);
        cancellationToken.ThrowIfCancellationRequested();

        return await scriptsLoader
            .TryExtendReadLockAsync(Db, keys.ReaderKey, lockId, ttl, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask ReleaseReadAsync(
        string resource,
        string lockId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(lockId);
        cancellationToken.ThrowIfCancellationRequested();

        _ = await scriptsLoader.ReleaseReadLockAsync(Db, keys.ReaderKey, lockId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> TryAcquireWriteAsync(
        string resource,
        string lockId,
        string waitingId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(lockId);
        _ValidateWaitingId(lockId, waitingId);
        cancellationToken.ThrowIfCancellationRequested();

        return await scriptsLoader
            .TryAcquireWriteLockAsync(Db, keys.WriterKey, keys.ReaderKey, lockId, waitingId, ttl, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> TryExtendWriteAsync(
        string resource,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(lockId);
        cancellationToken.ThrowIfCancellationRequested();

        return await scriptsLoader
            .TryExtendWriteLockAsync(Db, keys.WriterKey, lockId, ttl, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask ReleaseWriteAsync(
        string resource,
        string lockId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(lockId);
        cancellationToken.ThrowIfCancellationRequested();

        _ = await scriptsLoader
            .ReleaseWriteLockAsync(Db, keys.WriterKey, lockId, _GetWaitingId(lockId), cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> ValidateReadAsync(
        string resource,
        string lockId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(lockId);
        cancellationToken.ThrowIfCancellationRequested();

        return await Db.SetContainsAsync(keys.ReaderKey, lockId).ConfigureAwait(false);
    }

    public async ValueTask<bool> ValidateWriteAsync(
        string resource,
        string lockId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(lockId);
        cancellationToken.ThrowIfCancellationRequested();

        var value = await Db.StringGetAsync(keys.WriterKey).ConfigureAwait(false);

        return value.HasValue && string.Equals(value.ToString(), lockId, StringComparison.Ordinal);
    }

    public async ValueTask<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        var keys = _GetKeys(resource);
        cancellationToken.ThrowIfCancellationRequested();

        return await Db.SetLengthAsync(keys.ReaderKey).ConfigureAwait(false) > 0;
    }

    public async ValueTask<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        var keys = _GetKeys(resource);
        cancellationToken.ThrowIfCancellationRequested();

        var value = await Db.StringGetAsync(keys.WriterKey).ConfigureAwait(false);

        return value.HasValue && !value.ToString().EndsWith(WriterWaitingSuffix, StringComparison.Ordinal);
    }

    public async ValueTask<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        var keys = _GetKeys(resource);
        cancellationToken.ThrowIfCancellationRequested();

        return await Db.SetLengthAsync(keys.ReaderKey).ConfigureAwait(false);
    }

    private static (RedisKey WriterKey, RedisKey ReaderKey) _GetKeys(string resource)
    {
        Argument.IsNotNullOrEmpty(resource);
        Ensure.False(
            resource.Contains('{', StringComparison.Ordinal) || resource.Contains('}', StringComparison.Ordinal),
            "Reader-writer lock resources cannot contain '{' or '}' because Redis hash-tags are storage-owned."
        );

        var hashTag = "{" + resource + "}";

        return (hashTag + ":writer", hashTag + ":readers");
    }

    private static void _ValidateLockId(string lockId)
    {
        Argument.IsNotNullOrEmpty(lockId);
    }

    private static void _ValidateWaitingId(string lockId, string waitingId)
    {
        Argument.IsNotNullOrEmpty(waitingId);
        Ensure.True(
            string.Equals(waitingId, _GetWaitingId(lockId), StringComparison.Ordinal),
            "Writer waiting marker must be derived from the lock id."
        );
    }

    internal static string GetWaitingId(string lockId)
    {
        return _GetWaitingId(lockId);
    }

    private static string _GetWaitingId(string lockId)
    {
        return lockId + WriterWaitingSuffix;
    }
}
