// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Redis;
using System.Text;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis;

public sealed class RedisDistributedLockStorage(
    IConnectionMultiplexer multiplexer,
    HeadlessRedisScriptsLoader scriptsLoader
) : IDistributedLockStorage
{
    private const string PhysicalLockKeyPrefix = "{hflock:";
    private const string PhysicalLockKeySuffix = "}:value";
    private const string PhysicalLockKeyPattern = PhysicalLockKeyPrefix + "*" + PhysicalLockKeySuffix;

    private IDatabase Db => multiplexer.GetDatabase();

    public async ValueTask<DistributedLockAcquireResult> InsertAsync(
        string key,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var lockKey = _GetLockKey(key);
        var fenceKey = _GetFenceKey(key);

        var result = await scriptsLoader
            .TryAcquireLockAsync(Db, lockKey, fenceKey, lockId, ttl, cancellationToken)
            .ConfigureAwait(false);

        return result.Acquired
            ? new DistributedLockAcquireResult(Acquired: true, result.FencingToken)
            : DistributedLockAcquireResult.Failed;
    }

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

        return await scriptsLoader
            .ReplaceIfEqualAsync(Db, _GetLockKey(key), expectedId, newId, newTtl, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> RemoveIfEqualAsync(
        string key,
        string expectedId,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        return await scriptsLoader
            .RemoveIfEqualAsync(Db, _GetLockKey(key), expectedId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Db.KeyTimeToLiveAsync(_GetLockKey(key)).ConfigureAwait(false);
    }

    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Db.KeyExistsAsync(_GetLockKey(key)).ConfigureAwait(false);
    }

    public async ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value = await Db.StringGetAsync(_GetLockKey(key)).ConfigureAwait(false);

        return value.HasValue ? value.ToString() : null;
    }

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
                    .KeysAsync(pattern: PhysicalLockKeyPattern, pageSize: 1000)
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

    public async ValueTask<
        IReadOnlyDictionary<string, (string LockId, TimeSpan? Ttl)>
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
                    .KeysAsync(pattern: PhysicalLockKeyPattern, pageSize: 1000)
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
                .KeysAsync(pattern: PhysicalLockKeyPattern, pageSize: 1000)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false)
        )
        {
            var logicalKey = _TryGetLogicalKey(key);
            if (logicalKey is not null && logicalKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static RedisKey _GetFenceKey(string key)
    {
        return "fence:{" + _GetHashTag(key) + "}";
    }

    private static RedisKey _GetLockKey(string key)
    {
        return PhysicalLockKeyPrefix + _GetEncodedKey(key) + PhysicalLockKeySuffix;
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
            !physicalKey.StartsWith(PhysicalLockKeyPrefix, StringComparison.Ordinal)
            || !physicalKey.EndsWith(PhysicalLockKeySuffix, StringComparison.Ordinal)
        )
        {
            return null;
        }

        var encodedKey = physicalKey[
            PhysicalLockKeyPrefix.Length..^PhysicalLockKeySuffix.Length
        ];

        try
        {
            return Encoding.UTF8.GetString(Convert.FromHexString(encodedKey));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private async ValueTask _ProcessBatchAsync(
        List<RedisKey> batch,
        string prefix,
        Dictionary<string, string> result
    )
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
            if (value.HasValue && logicalKey is not null && logicalKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                result[logicalKey] = value.ToString();
            }
        }
    }

    private async ValueTask _ProcessBatchWithExpirationAsync(
        List<RedisKey> batch,
        string prefix,
        Dictionary<string, (string LockId, TimeSpan? Ttl)> result
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
            if (value.HasValue && logicalKey is not null && logicalKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                var ttl = await ttlTask.ConfigureAwait(false);
                result[logicalKey] = (value.ToString(), ttl);
            }
        }
    }
}
