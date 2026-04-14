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

        var server = multiplexer.GetServers()[0];
        var pattern = string.IsNullOrEmpty(prefix) ? "*" : $"{prefix}*";

        var keys = new List<RedisKey>();
        await foreach (
            var key in server
                .KeysAsync(pattern: pattern, pageSize: 1000)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false)
        )
        {
            keys.Add(key);
        }

        if (keys.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var keyArray = keys.ToArray();
        var values = await Db.StringGetAsync(keyArray).ConfigureAwait(false);
        var result = new Dictionary<string, string>(keyArray.Length, StringComparer.Ordinal);

        for (var i = 0; i < keyArray.Length; i++)
        {
            if (values[i].HasValue)
            {
                result[keyArray[i].ToString()] = values[i].ToString();
            }
        }

        return result;
    }

    public async ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var server = multiplexer.GetServers().First();
        var pattern = string.IsNullOrEmpty(prefix) ? "*" : $"{prefix}*";

        long count = 0;
        await foreach (
            var _ in server.KeysAsync(pattern: pattern).WithCancellation(cancellationToken).ConfigureAwait(false)
        )
        {
            count++;
        }

        return count;
    }
}
