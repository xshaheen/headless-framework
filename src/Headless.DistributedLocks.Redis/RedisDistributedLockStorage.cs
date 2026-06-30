// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.DistributedLocks.Redis.Scripts;
using Headless.Redis;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="IDistributedLockStorage"/> for exclusive (mutex) distributed
/// locks. Uses a single Lua script (<see cref="TryAcquireLockWithFenceScriptDefinition"/>) to atomically
/// SET the lock key NX and INCR a persistent fence-counter key, returning a monotonically increasing
/// fencing token on success. Lease renewal (extend) and release use CAS Lua scripts
/// (<see cref="ReplaceIfEqualScriptDefinition"/>, <see cref="RemoveIfEqualScriptDefinition"/>) so that
/// only the current lease holder can update or delete the key.
/// </summary>
/// <remarks>
/// Physical key layout: <c>{hflock:&lt;hex-encoded-key&gt;}:value</c> — the braces form a Redis
/// hash-tag so the lock key and its fence counter share the same cluster hash slot and can be
/// updated atomically within a single Lua script. Lock keys are enumerated via
/// <see cref="IServer.KeysAsync"/> on connected, non-replica endpoints using the pattern
/// <c>{hflock:*}:value</c>; value retrieval is issued as concurrent per-key GET commands so the
/// connection multiplexer can route them across cluster nodes correctly.
/// <para>
/// Underlying StackExchange.Redis errors (e.g. <see cref="StackExchange.Redis.RedisException"/>) propagate
/// to the caller unless explicitly caught by this class.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class RedisDistributedLockStorage(
    IConnectionMultiplexer multiplexer,
    [FromKeyedServices(RedisDistributedLockServiceKeys.ScriptsLoader)] HeadlessRedisScriptsLoader scriptsLoader
) : IDistributedLockStorage
{
    private const string _PhysicalLockKeyPrefix = "{hflock:";
    private const string _PhysicalLockKeySuffix = "}:value";
    private const string _PhysicalLockKeyPattern = _PhysicalLockKeyPrefix + "*" + _PhysicalLockKeySuffix;

    private IDatabase Db => multiplexer.GetDatabase();

    /// <summary>
    /// Atomically acquires an exclusive lock for <paramref name="key"/> identified by
    /// <paramref name="leaseId"/> and returns a fencing token on success.
    /// Uses <see cref="TryAcquireLockWithFenceScriptDefinition"/> — a Lua script that
    /// atomically executes SET NX and, on success, INCR on the fence counter key.
    /// </summary>
    /// <param name="key">The logical resource key to lock.</param>
    /// <param name="leaseId">A unique identifier for this lease; the caller is responsible for uniqueness.</param>
    /// <param name="ttl">Optional TTL for the lock key. When <see langword="null"/>, the lock persists until explicitly released.</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <returns>
    /// A <see cref="DistributedLockAcquireResult"/> with <c>Acquired = true</c> and a monotonically increasing
    /// fencing token when the lock is granted; <see cref="DistributedLockAcquireResult.Failed"/> when another
    /// holder already holds the key.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    /// <exception cref="StackExchange.Redis.RedisServerException">Thrown when the Lua acquire script returns an unexpected result format.</exception>
    public async ValueTask<DistributedLockAcquireResult> InsertAsync(
        string key,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var lockKey = _GetLockKey(key);
        var fenceKey = _GetFenceKey(key);

        var result = await _TryAcquireLockAsync(Db, lockKey, fenceKey, leaseId, ttl, cancellationToken)
            .ConfigureAwait(false);

        return result.Acquired
            ? new DistributedLockAcquireResult(Acquired: true, result.FencingToken)
            : DistributedLockAcquireResult.Failed;
    }

    /// <summary>
    /// Atomically replaces the lock value for <paramref name="key"/> with <paramref name="newId"/>
    /// only when the current stored value equals <paramref name="expectedId"/>.
    /// Uses <see cref="ReplaceIfEqualScriptDefinition"/> — a CAS Lua script. An empty string sentinel
    /// is used when <paramref name="expectedId"/> is <see langword="null"/>, meaning the key must not exist.
    /// </summary>
    /// <param name="key">The logical resource key.</param>
    /// <param name="expectedId">The lease id the current holder must match. Pass <see langword="null"/> to assert the key is absent.</param>
    /// <param name="newId">The new lease id to set on a successful compare.</param>
    /// <param name="newTtl">Optional TTL to apply to the key after a successful replace. <see langword="null"/> leaves the key persistent.</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <returns><see langword="true"/> when the compare-and-swap succeeded; <see langword="false"/> when the stored value did not match.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public async ValueTask<bool> ReplaceIfEqualAsync(
        string key,
        string expectedId,
        string newId,
        TimeSpan? newTtl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var parameters = _GetReplaceIfEqualParameters(_GetLockKey(key), newId, expectedId, newTtl);
        var result = await scriptsLoader
            .EvaluateAsync(Db, ReplaceIfEqualScriptDefinition.Instance, parameters, cancellationToken)
            .ConfigureAwait(false);

        return (int)result > 0;
    }

    /// <summary>
    /// Atomically deletes the lock key for <paramref name="key"/> only when the current stored value equals
    /// <paramref name="expectedId"/>. Uses <see cref="RemoveIfEqualScriptDefinition"/> — a CAS Lua script
    /// that issues DEL only when GET returns the expected value.
    /// </summary>
    /// <param name="key">The logical resource key.</param>
    /// <param name="expectedId">The lease id the current holder must match; the key is only deleted when the stored value equals this.</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <returns><see langword="true"/> when the key was deleted; <see langword="false"/> when the stored value did not match or the key did not exist.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public async ValueTask<bool> RemoveIfEqualAsync(
        string key,
        string expectedId,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                RemoveIfEqualScriptDefinition.Instance,
                new RemoveIfEqualParams(_GetLockKey(key), expectedId),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result > 0;
    }

    /// <summary>
    /// Returns the remaining TTL for the lock key associated with <paramref name="key"/> via Redis PTTL.
    /// </summary>
    /// <param name="key">The logical resource key.</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <returns>The remaining TTL, or <see langword="null"/> when the key does not exist or has no expiration.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public async ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Db.KeyTimeToLiveAsync(_GetLockKey(key)).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks whether the lock key for <paramref name="key"/> exists in Redis via EXISTS.
    /// </summary>
    /// <param name="key">The logical resource key.</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <returns><see langword="true"/> when the key exists; <see langword="false"/> otherwise.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Db.KeyExistsAsync(_GetLockKey(key)).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the lease id stored for the lock key associated with <paramref name="key"/> via Redis GETEX,
    /// or <see langword="null"/> when the key does not exist.
    /// </summary>
    /// <param name="key">The logical resource key.</param>
    /// <param name="cancellationToken">Token to cancel the operation before the Redis round-trip is issued.</param>
    /// <returns>The current lease id, or <see langword="null"/> when the lock is not held.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public async ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value = await Db.StringGetAsync(_GetLockKey(key)).ConfigureAwait(false);

        return value.HasValue ? value.ToString() : null;
    }

    /// <summary>
    /// Scans all connected, non-replica Redis endpoints for lock keys matching the physical pattern
    /// <c>{hflock:*}:value</c> and returns those whose decoded logical key starts with
    /// <paramref name="prefix"/>, along with their current lease id values.
    /// </summary>
    /// <remarks>
    /// Keys are enumerated via <see cref="IServer.KeysAsync"/> with a page size of 1000. Values are
    /// retrieved using concurrent per-key GET commands so the connection multiplexer can route them
    /// across cluster nodes. A <see langword="null"/> <paramref name="prefix"/> is treated as an
    /// empty string, matching all keys.
    /// </remarks>
    /// <param name="prefix">Logical key prefix filter. <see langword="null"/> matches all keys.</param>
    /// <param name="cancellationToken">Token to cancel key enumeration between pages.</param>
    /// <returns>A dictionary mapping logical key to current lease id for all matching live locks.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> fires during enumeration.</exception>
    public async ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        prefix ??= "";
        cancellationToken.ThrowIfCancellationRequested();

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var endpoint in multiplexer.GetEndPoints())
        {
            var server = multiplexer.GetServer(endpoint);
            if (server.IsReplica || !server.IsConnected)
            {
                continue;
            }

            var batch = new List<RedisKey>(1000);
            await foreach (
                var key in server
                    .KeysAsync(pattern: _PhysicalLockKeyPattern, pageSize: 1000)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                batch.Add(key);
                if (batch.Count >= 1000)
                {
                    await _ProcessBatchAsync(batch, prefix, result).ConfigureAwait(false);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await _ProcessBatchAsync(batch, prefix, result).ConfigureAwait(false);
            }
        }

        return result;
    }

    /// <summary>
    /// Scans all connected, non-replica Redis endpoints for lock keys matching the physical pattern
    /// <c>{hflock:*}:value</c> and returns those whose decoded logical key starts with
    /// <paramref name="prefix"/>, along with their current lease id and remaining TTL.
    /// </summary>
    /// <remarks>
    /// Keys are enumerated via <see cref="IServer.KeysAsync"/> with a page size of 1000. For each
    /// matching key, GET and PTTL are issued as concurrent tasks — the connection multiplexer handles
    /// cluster routing correctly. A <see langword="null"/> <paramref name="prefix"/> is treated as an
    /// empty string, matching all keys.
    /// </remarks>
    /// <param name="prefix">Logical key prefix filter. <see langword="null"/> matches all keys.</param>
    /// <param name="cancellationToken">Token to cancel key enumeration between pages.</param>
    /// <returns>A dictionary mapping logical key to <c>(LeaseId, Ttl)</c> for all matching live locks. <c>Ttl</c> is <see langword="null"/> when the key has no expiration.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> fires during enumeration.</exception>
    public async ValueTask<
        IReadOnlyDictionary<string, (string LeaseId, TimeSpan? Ttl)>
    > GetAllWithExpirationByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        prefix ??= "";
        cancellationToken.ThrowIfCancellationRequested();

        var result = new Dictionary<string, (string, TimeSpan?)>(StringComparer.Ordinal);

        foreach (var endpoint in multiplexer.GetEndPoints())
        {
            var server = multiplexer.GetServer(endpoint);
            if (server.IsReplica || !server.IsConnected)
            {
                continue;
            }

            var batch = new List<RedisKey>(1000);
            await foreach (
                var key in server
                    .KeysAsync(pattern: _PhysicalLockKeyPattern, pageSize: 1000)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                batch.Add(key);
                if (batch.Count >= 1000)
                {
                    await _ProcessBatchWithExpirationAsync(batch, prefix, result).ConfigureAwait(false);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await _ProcessBatchWithExpirationAsync(batch, prefix, result).ConfigureAwait(false);
            }
        }

        return result;
    }

    /// <summary>
    /// Counts active lock keys on all connected, non-replica endpoints whose decoded logical key
    /// starts with <paramref name="prefix"/>. Counts from each endpoint are summed.
    /// </summary>
    /// <param name="prefix">Logical key prefix filter. Empty string or <see langword="null"/> counts all locks.</param>
    /// <param name="cancellationToken">Token to cancel key enumeration between pages.</param>
    /// <returns>Total count of matching active lock keys across all connected endpoints. Returns 0 when no endpoints are connected.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> fires during enumeration.</exception>
    public async ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        prefix ??= "";
        cancellationToken.ThrowIfCancellationRequested();

        var endpoints = multiplexer.GetEndPoints();
        var tasks = new List<Task<long>>(endpoints.Length);

        foreach (var endpoint in endpoints)
        {
            var server = multiplexer.GetServer(endpoint);

            if (server.IsReplica || !server.IsConnected)
            {
                continue;
            }

            tasks.Add(_CountKeysByPrefixAsync(server, prefix, cancellationToken));
        }

        if (tasks.Count is 0)
        {
            return 0;
        }

        var counts = await Task.WhenAll(tasks).ConfigureAwait(false);
        return counts.Sum();
    }

    private static async Task<long> _CountKeysByPrefixAsync(
        IServer server,
        string prefix,
        CancellationToken cancellationToken
    )
    {
        long count = 0;

        await foreach (
            var key in server
                .KeysAsync(pattern: _PhysicalLockKeyPattern, pageSize: 1000)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false)
        )
        {
            var logicalKey = _TryGetLogicalKey(key);

            if (logicalKey?.StartsWith(prefix, StringComparison.Ordinal) == true)
            {
                count++;
            }
        }

        return count;
    }

    private async Task<(bool Acquired, long? FencingToken)> _TryAcquireLockAsync(
        IDatabase db,
        RedisKey key,
        RedisKey fenceKey,
        string leaseId,
        TimeSpan? ttl,
        CancellationToken cancellationToken
    )
    {
        var parameters = _GetAcquireLockParameters(key, fenceKey, leaseId, ttl);
        var result = await scriptsLoader
            .EvaluateAsync(db, TryAcquireLockWithFenceScriptDefinition.Instance, parameters, cancellationToken)
            .ConfigureAwait(false);
        var values = (RedisResult[]?)result;

        if (values is null || values.Length == 0)
        {
            throw new RedisServerException("Unexpected acquire lock script result.");
        }

        if ((int)values[0] <= 0)
        {
            return (false, null);
        }

        if (values.Length < 2)
        {
            throw new RedisServerException("Acquire lock script reported success without a fencing token.");
        }

        return (true, (long)values[1]);
    }

    private static ReplaceIfEqualParams _GetReplaceIfEqualParameters(
        RedisKey key,
        string? value,
        string? expected,
        TimeSpan? expires
    )
    {
        // Use empty string as sentinel for null expected (key should not exist).
        var expectedValue = expected ?? string.Empty;
        var expiresValue = expires.HasValue ? (int)expires.Value.TotalMilliseconds : RedisValue.EmptyString;

        return new ReplaceIfEqualParams(key, value, expectedValue, expiresValue);
    }

    private static AcquireLockParams _GetAcquireLockParameters(
        RedisKey key,
        RedisKey fenceKey,
        string leaseId,
        TimeSpan? expires
    )
    {
        var expiresValue = expires.HasValue ? (int)expires.Value.TotalMilliseconds : RedisValue.EmptyString;

        return new AcquireLockParams(key, fenceKey, leaseId, expiresValue);
    }

    private static RedisKey _GetFenceKey(string key)
    {
        return "fence:{" + _GetHashTag(key) + "}";
    }

    private static RedisKey _GetLockKey(string key)
    {
        return _PhysicalLockKeyPrefix + _GetEncodedKey(key) + _PhysicalLockKeySuffix;
    }

    private static string _GetHashTag(string key)
    {
        return "hflock:" + _GetEncodedKey(key);
    }

    private static string _GetEncodedKey(string key)
    {
        return Convert.ToHexString(Encoding.UTF8.GetBytes(key));
    }

    private static string? _TryGetLogicalKey(RedisKey key)
    {
        var physicalKey = key.ToString();

        if (
            !physicalKey.StartsWith(_PhysicalLockKeyPrefix, StringComparison.Ordinal)
            || !physicalKey.EndsWith(_PhysicalLockKeySuffix, StringComparison.Ordinal)
        )
        {
            return null;
        }

        var encodedKey = physicalKey[_PhysicalLockKeyPrefix.Length..^_PhysicalLockKeySuffix.Length];

        try
        {
            return Encoding.UTF8.GetString(Convert.FromHexString(encodedKey));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private async ValueTask _ProcessBatchAsync(List<RedisKey> batch, string prefix, Dictionary<string, string> result)
    {
        // NOTE: We use individual async tasks instead of Db.StringGetAsync(keyArray)
        // for absolute cluster safety across any sharding topology.
        // StackExchange.Redis automatically pipelines these concurrent requests
        // while correctly routing them to the appropriate nodes based on hash slots.
        var tasks = new List<(RedisKey Key, Task<RedisValue> ValueTask)>(batch.Count);

        foreach (var key in batch)
        {
            tasks.Add((key, Db.StringGetAsync(key)));
        }

        var allTasks = new List<Task>(batch.Count);
        foreach (var tuple in tasks)
        {
            allTasks.Add(tuple.ValueTask);
        }

        await Task.WhenAll(allTasks).ConfigureAwait(false);

        foreach (var (key, valueTask) in tasks)
        {
            var value = await valueTask.ConfigureAwait(false);
            var logicalKey = _TryGetLogicalKey(key);
            if (value.HasValue && logicalKey?.StartsWith(prefix, StringComparison.Ordinal) == true)
            {
                result[logicalKey] = value.ToString();
            }
        }
    }

    private async ValueTask _ProcessBatchWithExpirationAsync(
        List<RedisKey> batch,
        string prefix,
        Dictionary<string, (string LeaseId, TimeSpan? Ttl)> result
    )
    {
        // NOTE: We avoid IDatabase.CreateBatch() because IBatch is bound to a single node.
        // Using individual tasks allows the multiplexer to handle routing naturally
        // while still benefiting from network-level pipelining.
        var tasks = new List<(RedisKey Key, Task<RedisValue> ValueTask, Task<TimeSpan?> TtlTask)>(batch.Count);

        foreach (var key in batch)
        {
            tasks.Add((key, Db.StringGetAsync(key), Db.KeyTimeToLiveAsync(key)));
        }

        var allTasks = new List<Task>(batch.Count * 2);
        foreach (var tuple in tasks)
        {
            allTasks.Add(tuple.ValueTask);
            allTasks.Add(tuple.TtlTask);
        }

        await Task.WhenAll(allTasks).ConfigureAwait(false);

        foreach (var (key, valueTask, ttlTask) in tasks)
        {
            var value = await valueTask.ConfigureAwait(false);
            var logicalKey = _TryGetLogicalKey(key);

            if (value.HasValue && logicalKey?.StartsWith(prefix, StringComparison.Ordinal) == true)
            {
                var ttl = await ttlTask.ConfigureAwait(false);
                result[logicalKey] = (value.ToString(), ttl);
            }
        }
    }
}
