// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis;

public sealed class RedisDistributedLockStorage(
    IConnectionMultiplexer multiplexer,
    HeadlessRedisScriptsLoader scriptsLoader
) : IDistributedLockStorage
{
    private IDatabase Db => multiplexer.GetDatabase();

    public async ValueTask<bool> InsertAsync(
        string key,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        return await Db.StringSetAsync(key, lockId, ttl, When.NotExists, CommandFlags.None).ConfigureAwait(false);
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
            .ReplaceIfEqualAsync(Db, key, expectedId, newId, newTtl, cancellationToken)
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

        return await scriptsLoader.RemoveIfEqualAsync(Db, key, expectedId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Db.KeyTimeToLiveAsync(key).ConfigureAwait(false);
    }

    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Db.KeyExistsAsync(key).ConfigureAwait(false);
    }

    public async ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value = await Db.StringGetAsync(key).ConfigureAwait(false);

        return value.HasValue ? value.ToString() : null;
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pattern = string.IsNullOrEmpty(prefix) ? "*" : $"{prefix}*";
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
                    .KeysAsync(pattern: pattern, pageSize: 1000)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                batch.Add(key);
                if (batch.Count >= 1000)
                {
                    await _ProcessBatchAsync(batch, result).ConfigureAwait(false);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await _ProcessBatchAsync(batch, result).ConfigureAwait(false);
            }
        }

        return result;
    }

    public async ValueTask<
        IReadOnlyDictionary<string, (string LockId, TimeSpan? Ttl)>
    > GetAllWithExpirationByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pattern = string.IsNullOrEmpty(prefix) ? "*" : $"{prefix}*";
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
                    .KeysAsync(pattern: pattern, pageSize: 1000)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                batch.Add(key);
                if (batch.Count >= 1000)
                {
                    await _ProcessBatchWithExpirationAsync(batch, result).ConfigureAwait(false);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await _ProcessBatchWithExpirationAsync(batch, result).ConfigureAwait(false);
            }
        }

        return result;
    }

    public async ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pattern = string.IsNullOrEmpty(prefix) ? "*" : $"{prefix}*";
        long totalCount = 0;

        foreach (var endpoint in multiplexer.GetEndPoints())
        {
            var server = multiplexer.GetServer(endpoint);
            if (server.IsReplica || !server.IsConnected)
            {
                continue;
            }

            await foreach (
                var _ in server.KeysAsync(pattern: pattern).WithCancellation(cancellationToken).ConfigureAwait(false)
            )
            {
                totalCount++;
            }
        }

        return totalCount;
    }

    private async ValueTask _ProcessBatchAsync(List<RedisKey> batch, Dictionary<string, string> result)
    {
        var keyArray = batch.ToArray();
        var values = await Db.StringGetAsync(keyArray).ConfigureAwait(false);

        for (var i = 0; i < keyArray.Length; i++)
        {
            if (values[i].HasValue)
            {
                result[keyArray[i].ToString()] = values[i].ToString();
            }
        }
    }

    private async ValueTask _ProcessBatchWithExpirationAsync(
        List<RedisKey> batch,
        Dictionary<string, (string LockId, TimeSpan? Ttl)> result
    )
    {
        var batchCommands = Db.CreateBatch();
        var tasks = new List<(RedisKey Key, Task<RedisValue> ValueTask, Task<TimeSpan?> TtlTask)>(batch.Count);

        foreach (var key in batch)
        {
            tasks.Add((key, batchCommands.StringGetAsync(key), batchCommands.KeyTimeToLiveAsync(key)));
        }

        batchCommands.Execute();

        foreach (var (key, valueTask, ttlTask) in tasks)
        {
            var value = await valueTask.ConfigureAwait(false);
            if (value.HasValue)
            {
                var ttl = await ttlTask.ConfigureAwait(false);
                result[key.ToString()] = (value.ToString(), ttl);
            }
        }
    }
}
