// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Redis;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="IDistributedReadWriteLockStorage"/> for distributed
/// reader-writer locks with writer preference. The key layout uses a Redis hash-tag so all
/// keys for a resource share the same cluster hash slot, enabling atomic Lua scripts:
/// <list type="bullet">
///   <item><c>{resource}:writer</c> — STRING key holding the current writer's lease id, or the
///   writer-waiting marker (<c>{waitingId}:_WRITERWAITING</c>) when a writer is queued.</item>
///   <item><c>{resource}:readers</c> — HASH key mapping reader lease id to its expiry timestamp
///   in milliseconds (or <c>"0"</c> for infinite readers).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// The resource name must not contain <c>{</c> or <c>}</c> characters — the key layout wraps
/// the resource in a Redis hash-tag which is storage-owned.
/// </para>
/// <para>
/// Lease ids must not contain <c>:</c> because the writer-waiting marker appends <c>:_WRITERWAITING</c>
/// as a suffix and the suffix delimiter is a colon.
/// </para>
/// <para>
/// StackExchange.Redis does not expose request-level cancellation on non-scripted IDatabase methods
/// (e.g. <c>HashExistsAsync</c>, <c>StringGetAsync</c>, <c>HashLengthAsync</c>). These methods
/// eagerly throw on a pre-cancelled token and wrap the awaitable with
/// <see cref="Task.WaitAsync(CancellationToken)"/> to preempt the await when the token fires mid-flight.
/// The underlying Redis request continues to completion in that case; hard bounds are supplied
/// by StackExchange.Redis's own <c>AsyncTimeout</c> setting.
/// </para>
/// <para>
/// Underlying StackExchange.Redis errors (e.g. <see cref="StackExchange.Redis.RedisException"/>) propagate
/// to the caller unless explicitly caught by this class.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class RedisDistributedReadWriteLockStorage(
    IConnectionMultiplexer multiplexer,
    [FromKeyedServices(RedisDistributedLockServiceKeys.ScriptsLoader)] HeadlessRedisScriptsLoader scriptsLoader
) : IDistributedReadWriteLockStorage
{
    private IDatabase Db => multiplexer.GetDatabase();

    /// <summary>
    /// Atomically acquires a shared read lease for <paramref name="resource"/> identified by
    /// <paramref name="leaseId"/> when no writer (or writer-waiting marker) holds the resource.
    /// Uses <see cref="TryAcquireReadLockScriptDefinition"/> — a Lua script that checks the writer
    /// key then records the reader in the readers HASH with an expiry timestamp score.
    /// </summary>
    /// <param name="resource">The logical resource name. Must not contain <c>{</c> or <c>}</c>.</param>
    /// <param name="leaseId">A unique identifier for this read lease. Must not contain <c>:</c>.</param>
    /// <param name="ttl">Optional read lease TTL. When <see langword="null"/>, the reader entry is stored with score <c>0</c> (infinite).</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <returns><see langword="true"/> when the read lease is granted; <see langword="false"/> on writer contention.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="leaseId"/> is empty, contains <c>:</c>, <paramref name="resource"/> is null/empty, or <paramref name="resource"/> contains <c>{</c> or <c>}</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public async ValueTask<bool> TryAcquireReadAsync(
        string resource,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                TryAcquireReadLockScriptDefinition.Instance,
                _GetReadLockParameters(keys.WriterKey, keys.ReaderKey, leaseId, ttl),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result > 0;
    }

    /// <summary>
    /// Atomically refreshes the TTL of the caller's read lease. Uses
    /// <see cref="TryExtendReadLockScriptDefinition"/> — a Lua script that checks for the reader id
    /// in the hash (via HEXISTS), refuses to extend when a writer-waiting marker is present, then
    /// updates the per-entry expiry score and the key's safety TTL.
    /// </summary>
    /// <param name="resource">The logical resource name. Must not contain <c>{</c> or <c>}</c>.</param>
    /// <param name="leaseId">The lease id of the read lease to extend. Must not contain <c>:</c>.</param>
    /// <param name="ttl">New TTL from now. When <see langword="null"/>, the implementation does not update the score.</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <returns><see langword="true"/> when the reader id was found and the TTL was extended; <see langword="false"/> when the lease is no longer held or a writer is waiting.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="leaseId"/> is empty, contains <c>:</c>, <paramref name="resource"/> is null/empty, or <paramref name="resource"/> contains <c>{</c> or <c>}</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public async ValueTask<bool> TryExtendReadAsync(
        string resource,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                TryExtendReadLockScriptDefinition.Instance,
                _GetReadLockParameters(keys.WriterKey, keys.ReaderKey, leaseId, ttl),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result > 0;
    }

    /// <summary>
    /// Removes the caller's read lease id from the readers HASH for <paramref name="resource"/>.
    /// Uses <see cref="ReleaseReadLockScriptDefinition"/> — a Lua script that issues HDEL. Idempotent:
    /// no error is raised when the field is not present.
    /// </summary>
    /// <param name="resource">The logical resource name. Must not contain <c>{</c> or <c>}</c>.</param>
    /// <param name="leaseId">The lease id of the read lease to release. Must not contain <c>:</c>.</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="leaseId"/> is empty, contains <c>:</c>, <paramref name="resource"/> is null/empty, or <paramref name="resource"/> contains <c>{</c> or <c>}</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public async ValueTask ReleaseReadAsync(
        string resource,
        string leaseId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        _ = await scriptsLoader
            .EvaluateAsync(
                Db,
                ReleaseReadLockScriptDefinition.Instance,
                _GetReaderOnlyLockParameters(keys.ReaderKey, leaseId, ttl: null),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Atomically acquires an exclusive write lease for <paramref name="resource"/> or plants the
    /// writer-waiting marker when readers are present (writer preference per D8). Uses
    /// <see cref="TryAcquireWriteLockScriptDefinition"/> — a Lua script that prunes expired reader
    /// entries (HGETALL + per-field HDEL), then either sets the writer key to <paramref name="leaseId"/>
    /// when no readers remain, or sets it to <paramref name="waitingId"/> suffixed with
    /// <c>:_WRITERWAITING</c> when readers block the promotion.
    /// </summary>
    /// <param name="resource">The logical resource name. Must not contain <c>{</c> or <c>}</c>.</param>
    /// <param name="leaseId">The unique write lease id. Must not contain <c>:</c>.</param>
    /// <param name="waitingId">The writer-waiting marker id (typically derived from <paramref name="leaseId"/>). Must not be <see langword="null"/> or empty.</param>
    /// <param name="ttl">Optional TTL for the writer key when the exclusive lock is granted. <see langword="null"/> means no expiry.</param>
    /// <param name="markerTtl">Optional TTL for the writer-waiting marker. <see langword="null"/> means no expiry on the marker.</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <returns><see langword="true"/> when the exclusive write lease is granted; <see langword="false"/> when readers are present or another writer holds the resource.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leaseId"/> or <paramref name="waitingId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="leaseId"/> is empty, contains <c>:</c>, <paramref name="waitingId"/> is empty, <paramref name="resource"/> is null/empty, or <paramref name="resource"/> contains <c>{</c> or <c>}</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public async ValueTask<bool> TryAcquireWriteAsync(
        string resource,
        string leaseId,
        string waitingId,
        TimeSpan? ttl = null,
        TimeSpan? markerTtl = null,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(leaseId);
        Argument.IsNotNullOrEmpty(waitingId);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                TryAcquireWriteLockScriptDefinition.Instance,
                _GetWriteLockParameters(keys.WriterKey, keys.ReaderKey, leaseId, waitingId, ttl, markerTtl),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result > 0;
    }

    /// <summary>
    /// Atomically refreshes the TTL of the caller's exclusive write lease by issuing PEXPIRE on the
    /// writer key when its current value matches <paramref name="leaseId"/>. Uses
    /// <see cref="TryExtendWriteLockScriptDefinition"/> — a Lua script that compares the stored value
    /// then conditionally applies PEXPIRE.
    /// </summary>
    /// <param name="resource">The logical resource name. Must not contain <c>{</c> or <c>}</c>.</param>
    /// <param name="leaseId">The lease id of the write lease to extend. Must not contain <c>:</c>.</param>
    /// <param name="ttl">New TTL from now. When <see langword="null"/>, PEXPIRE is not called.</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <returns><see langword="true"/> when the stored writer id matches and the TTL was extended; <see langword="false"/> when the lease is no longer held.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="leaseId"/> is empty, contains <c>:</c>, <paramref name="resource"/> is null/empty, or <paramref name="resource"/> contains <c>{</c> or <c>}</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public async ValueTask<bool> TryExtendWriteAsync(
        string resource,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                TryExtendWriteLockScriptDefinition.Instance,
                _GetWriterOnlyLockParameters(keys.WriterKey, leaseId, ttl),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result > 0;
    }

    /// <summary>
    /// Releases the caller's exclusive write lease or the writer-waiting marker for
    /// <paramref name="resource"/>. Uses <see cref="ReleaseWriteLockScriptDefinition"/> — a Lua script
    /// that deletes the writer key when its current value is either <paramref name="leaseId"/> (held
    /// writer) or the waiting marker derived from it (<c>waitingId</c> passed by the caller), so an
    /// abandoned queued writer doesn't strand the resource until TTL expiry. Idempotent.
    /// </summary>
    /// <param name="resource">The logical resource name. Must not contain <c>{</c> or <c>}</c>.</param>
    /// <param name="leaseId">The write lease id to release. Must not contain <c>:</c>.</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="leaseId"/> is empty, contains <c>:</c>, <paramref name="resource"/> is null/empty, or <paramref name="resource"/> contains <c>{</c> or <c>}</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public async ValueTask ReleaseWriteAsync(
        string resource,
        string leaseId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        _ = await scriptsLoader
            .EvaluateAsync(
                Db,
                ReleaseWriteLockScriptDefinition.Instance,
                _GetWriterOnlyLockParameters(
                    keys.WriterKey,
                    leaseId,
                    ttl: null,
                    DistributedLockCoreHelpers.GetWriterWaitingId(leaseId)
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

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="leaseId"/> is currently present in the
    /// readers HASH for <paramref name="resource"/> via HEXISTS. Intended for self-validation by a
    /// monitoring loop; result is advisory — the TTL can expire between this read and any subsequent
    /// action.
    /// </summary>
    /// <param name="resource">The logical resource name. Must not contain <c>{</c> or <c>}</c>.</param>
    /// <param name="leaseId">The lease id to check for in the readers HASH. Must not contain <c>:</c>.</param>
    /// <param name="cancellationToken">Token to cancel the operation; preempts the in-flight await via <see cref="Task.WaitAsync(CancellationToken)"/>.</param>
    /// <returns><see langword="true"/> when the reader id is found in the readers HASH; <see langword="false"/> otherwise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="leaseId"/> is empty, contains <c>:</c>, <paramref name="resource"/> is null/empty, or <paramref name="resource"/> contains <c>{</c> or <c>}</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> fires (eagerly or during the await).</exception>
    public async ValueTask<bool> ValidateReadAsync(
        string resource,
        string leaseId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        return await Db.HashExistsAsync(keys.ReaderKey, leaseId).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the stored writer key for <paramref name="resource"/> matches
    /// <paramref name="leaseId"/> exactly (excluding the writer-waiting marker) via GET. Intended for
    /// self-validation by a monitoring loop; result is advisory — the TTL can expire between this read
    /// and any subsequent action.
    /// </summary>
    /// <param name="resource">The logical resource name. Must not contain <c>{</c> or <c>}</c>.</param>
    /// <param name="leaseId">The lease id to compare against the stored writer value. Must not contain <c>:</c>.</param>
    /// <param name="cancellationToken">Token to cancel the operation; preempts the in-flight await via <see cref="Task.WaitAsync(CancellationToken)"/>.</param>
    /// <returns><see langword="true"/> when the stored value equals <paramref name="leaseId"/>; <see langword="false"/> otherwise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leaseId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="leaseId"/> is empty, contains <c>:</c>, <paramref name="resource"/> is null/empty, or <paramref name="resource"/> contains <c>{</c> or <c>}</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> fires (eagerly or during the await).</exception>
    public async ValueTask<bool> ValidateWriteAsync(
        string resource,
        string leaseId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        var value = await Db.StringGetAsync(keys.WriterKey).WaitAsync(cancellationToken).ConfigureAwait(false);

        return value.HasValue && string.Equals(value.ToString(), leaseId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the readers HASH for <paramref name="resource"/> is
    /// non-empty via HLEN. Point-in-time only — expired but unpruned entries may be included.
    /// Use for diagnostics only; do not rely on this for correctness decisions.
    /// </summary>
    /// <param name="resource">The logical resource name. Must not contain <c>{</c> or <c>}</c>.</param>
    /// <param name="cancellationToken">Token to cancel the operation; preempts the in-flight await via <see cref="Task.WaitAsync(CancellationToken)"/>.</param>
    /// <returns><see langword="true"/> when at least one entry is in the readers HASH; <see langword="false"/> otherwise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty or contains <c>{</c> or <c>}</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> fires (eagerly or during the await).</exception>
    public async ValueTask<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        var keys = _GetKeys(resource);
        cancellationToken.ThrowIfCancellationRequested();

        return await Db.HashLengthAsync(keys.ReaderKey).WaitAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the writer key for <paramref name="resource"/> exists and
    /// its value does NOT end with <c>:_WRITERWAITING</c> (i.e. a real writer — not just a queued
    /// marker — holds the resource). Point-in-time only; use for diagnostics.
    /// </summary>
    /// <param name="resource">The logical resource name. Must not contain <c>{</c> or <c>}</c>.</param>
    /// <param name="cancellationToken">Token to cancel the operation; preempts the in-flight await via <see cref="Task.WaitAsync(CancellationToken)"/>.</param>
    /// <returns><see langword="true"/> when a promoted writer holds the resource; <see langword="false"/> when no writer or only a writer-waiting marker is present.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty or contains <c>{</c> or <c>}</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> fires (eagerly or during the await).</exception>
    public async ValueTask<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        var keys = _GetKeys(resource);
        cancellationToken.ThrowIfCancellationRequested();

        var value = await Db.StringGetAsync(keys.WriterKey).WaitAsync(cancellationToken).ConfigureAwait(false);

        return value.HasValue
            && !value.ToString().EndsWith(DistributedLockCoreHelpers.WriterWaitingSuffix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns the number of entries in the readers HASH for <paramref name="resource"/> via HLEN.
    /// Point-in-time only — expired but unpruned entries may be included. Use for diagnostics only;
    /// do not rely on this for correctness decisions.
    /// </summary>
    /// <param name="resource">The logical resource name. Must not contain <c>{</c> or <c>}</c>.</param>
    /// <param name="cancellationToken">Token to cancel the operation; preempts the in-flight await via <see cref="Task.WaitAsync(CancellationToken)"/>.</param>
    /// <returns>Number of entries in the readers HASH (including expired-but-unpruned entries). Returns 0 when no readers are registered.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is empty or contains <c>{</c> or <c>}</c>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> fires (eagerly or during the await).</exception>
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
        string leaseId,
        TimeSpan? ttl
    )
    {
        var expiresValue = ttl.HasValue ? (int)ttl.Value.TotalMilliseconds : RedisValue.EmptyString;

        return new ReaderWriterReadParams(writerKey, readerKey, leaseId, expiresValue);
    }

    private static ReaderWriterReaderOnlyParams _GetReaderOnlyLockParameters(
        RedisKey readerKey,
        string leaseId,
        TimeSpan? ttl
    )
    {
        var expiresValue = ttl.HasValue ? (int)ttl.Value.TotalMilliseconds : RedisValue.EmptyString;

        return new ReaderWriterReaderOnlyParams(readerKey, leaseId, expiresValue);
    }

    private static ReaderWriterWriteParams _GetWriteLockParameters(
        RedisKey writerKey,
        RedisKey readerKey,
        string leaseId,
        string waitingId,
        TimeSpan? ttl,
        TimeSpan? markerTtl
    )
    {
        var expiresValue = ttl.HasValue ? (int)ttl.Value.TotalMilliseconds : RedisValue.EmptyString;
        var markerExpiresValue = markerTtl.HasValue ? (int)markerTtl.Value.TotalMilliseconds : RedisValue.EmptyString;

        return new ReaderWriterWriteParams(writerKey, readerKey, leaseId, waitingId, expiresValue, markerExpiresValue);
    }

    private static ReaderWriterWriterOnlyParams _GetWriterOnlyLockParameters(
        RedisKey writerKey,
        string leaseId,
        TimeSpan? ttl,
        string? waitingId = null
    )
    {
        var expiresValue = ttl.HasValue ? (int)ttl.Value.TotalMilliseconds : RedisValue.EmptyString;

        return new ReaderWriterWriterOnlyParams(writerKey, leaseId, waitingId ?? string.Empty, expiresValue);
    }

    private static void _ValidateLockId(string leaseId)
    {
        Argument.IsNotNullOrEmpty(leaseId);
        Ensure.False(
            leaseId.Contains(':', StringComparison.Ordinal),
            "Reader-writer lock ids cannot contain ':' because it conflicts with the writer-waiting suffix delimiter."
        );
    }
}
