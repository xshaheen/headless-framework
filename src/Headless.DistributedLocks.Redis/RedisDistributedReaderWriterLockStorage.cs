// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Redis;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis;

public sealed class RedisDistributedReaderWriterLockStorage(
    IConnectionMultiplexer multiplexer,
    [FromKeyedServices(RedisDistributedLockServiceKeys.ScriptsLoader)] HeadlessRedisScriptsLoader scriptsLoader
) : IDistributedReaderWriterLockStorage
{
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

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                TryAcquireReadLockScriptDefinition.Instance,
                _GetReadLockParameters(keys.WriterKey, keys.ReaderKey, lockId, ttl),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result > 0;
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

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                TryExtendReadLockScriptDefinition.Instance,
                _GetReadLockParameters(keys.WriterKey, keys.ReaderKey, lockId, ttl),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result > 0;
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

        _ = await scriptsLoader
            .EvaluateAsync(
                Db,
                ReleaseReadLockScriptDefinition.Instance,
                _GetReaderOnlyLockParameters(keys.ReaderKey, lockId, ttl: null),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> TryAcquireWriteAsync(
        string resource,
        string lockId,
        string waitingId,
        TimeSpan? ttl = null,
        TimeSpan? markerTtl = null,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(lockId);
        Argument.IsNotNullOrEmpty(waitingId);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                TryAcquireWriteLockScriptDefinition.Instance,
                _GetWriteLockParameters(keys.WriterKey, keys.ReaderKey, lockId, waitingId, ttl, markerTtl),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result > 0;
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

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                TryExtendWriteLockScriptDefinition.Instance,
                _GetWriterOnlyLockParameters(keys.WriterKey, lockId, ttl),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result > 0;
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
            .EvaluateAsync(
                Db,
                ReleaseWriteLockScriptDefinition.Instance,
                _GetWriterOnlyLockParameters(
                    keys.WriterKey,
                    lockId,
                    ttl: null,
                    DistributedLockCoreHelpers.GetWriterWaitingId(lockId)
                ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    // StackExchange.Redis's IDatabase APIs (HashExistsAsync/StringGetAsync/HashLengthAsync) do
    // not accept a CancellationToken — the driver does not expose request-level cancellation. We
    // (1) throw eagerly on a pre-cancelled token to avoid issuing the round trip and
    // (2) wrap the awaitable with Task.WaitAsync(cancellationToken) so a token that fires while
    // the round trip is in flight still preempts the await. The Redis request itself keeps
    // running to completion — consumers that need request-level cancellation must rely on the
    // StackExchange.Redis AsyncTimeout for hard bounds.
    public async ValueTask<bool> ValidateReadAsync(
        string resource,
        string lockId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(lockId);
        cancellationToken.ThrowIfCancellationRequested();

        return await Db.HashExistsAsync(keys.ReaderKey, lockId).WaitAsync(cancellationToken).ConfigureAwait(false);
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

        var value = await Db.StringGetAsync(keys.WriterKey).WaitAsync(cancellationToken).ConfigureAwait(false);

        return value.HasValue && string.Equals(value.ToString(), lockId, StringComparison.Ordinal);
    }

    public async ValueTask<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        var keys = _GetKeys(resource);
        cancellationToken.ThrowIfCancellationRequested();

        return await Db.HashLengthAsync(keys.ReaderKey).WaitAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async ValueTask<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        var keys = _GetKeys(resource);
        cancellationToken.ThrowIfCancellationRequested();

        var value = await Db.StringGetAsync(keys.WriterKey).WaitAsync(cancellationToken).ConfigureAwait(false);

        return value.HasValue
            && !value.ToString().EndsWith(DistributedLockCoreHelpers.WriterWaitingSuffix, StringComparison.Ordinal);
    }

    public async ValueTask<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        var keys = _GetKeys(resource);
        cancellationToken.ThrowIfCancellationRequested();

        return await Db.HashLengthAsync(keys.ReaderKey).WaitAsync(cancellationToken).ConfigureAwait(false);
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

    private static ReaderWriterReadParams _GetReadLockParameters(
        RedisKey writerKey,
        RedisKey readerKey,
        string lockId,
        TimeSpan? ttl
    )
    {
        var expiresValue = ttl.HasValue ? (int)ttl.Value.TotalMilliseconds : RedisValue.EmptyString;

        return new ReaderWriterReadParams(writerKey, readerKey, lockId, expiresValue);
    }

    private static ReaderWriterReaderOnlyParams _GetReaderOnlyLockParameters(
        RedisKey readerKey,
        string lockId,
        TimeSpan? ttl
    )
    {
        var expiresValue = ttl.HasValue ? (int)ttl.Value.TotalMilliseconds : RedisValue.EmptyString;

        return new ReaderWriterReaderOnlyParams(readerKey, lockId, expiresValue);
    }

    private static ReaderWriterWriteParams _GetWriteLockParameters(
        RedisKey writerKey,
        RedisKey readerKey,
        string lockId,
        string waitingId,
        TimeSpan? ttl,
        TimeSpan? markerTtl
    )
    {
        var expiresValue = ttl.HasValue ? (int)ttl.Value.TotalMilliseconds : RedisValue.EmptyString;
        var markerExpiresValue = markerTtl.HasValue
            ? (int)markerTtl.Value.TotalMilliseconds
            : RedisValue.EmptyString;

        return new ReaderWriterWriteParams(writerKey, readerKey, lockId, waitingId, expiresValue, markerExpiresValue);
    }

    private static ReaderWriterWriterOnlyParams _GetWriterOnlyLockParameters(
        RedisKey writerKey,
        string lockId,
        TimeSpan? ttl,
        string? waitingId = null
    )
    {
        var expiresValue = ttl.HasValue ? (int)ttl.Value.TotalMilliseconds : RedisValue.EmptyString;

        return new ReaderWriterWriterOnlyParams(writerKey, lockId, waitingId ?? string.Empty, expiresValue);
    }

    private static void _ValidateLockId(string lockId)
    {
        Argument.IsNotNullOrEmpty(lockId);
        Ensure.False(
            lockId.Contains(':', StringComparison.Ordinal),
            "Reader-writer lock ids cannot contain ':' because it conflicts with the writer-waiting suffix delimiter."
        );
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ReaderWriterReadParams(
        RedisKey writerKey,
        RedisKey readerKey,
        string lockId,
        RedisValue expires
    );

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ReaderWriterReaderOnlyParams(RedisKey readerKey, string lockId, RedisValue expires);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ReaderWriterWriteParams(
        RedisKey writerKey,
        RedisKey readerKey,
        string lockId,
        string waitingId,
        RedisValue expires,
        RedisValue markerExpires
    );

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ReaderWriterWriterOnlyParams(
        RedisKey writerKey,
        string lockId,
        string waitingId,
        RedisValue expires
    );
}
