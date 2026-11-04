// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Caching;
using Headless.Serializer;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisCacheExpiredCasDeleteTests(RedisCacheFixture fixture) : RedisCacheTestBase(fixture)
{
    [Fact]
    public async Task should_keep_a_concurrent_writers_value_when_an_expired_cas_write_deletes_the_entry()
    {
        // given - an entry the caller read, and a factory result for it that is already expired
        await FlushAsync();
        using var writer = CreateCache();
        var store = (IFactoryCacheStore)writer;
        var key = Faker.Random.AlphaNumeric(10);
        var now = DateTime.UtcNow;
        var original = new CacheStoreEntryWrite<string>
        {
            Value = "original",
            IsNull = false,
            LogicalExpiresAt = now.AddMinutes(5),
            PhysicalExpiresAt = now.AddMinutes(5),
            CreatedAt = now,
        };
        await store.SetEntryAsync(key, in original, AbortToken);
        var snapshot = await store.TryGetEntryAsync<string>(key, cancellationToken: AbortToken);

        // A second writer lands once the expiring caller starts its delete: after it read the key, or right before
        // the command that compares and deletes. Either way it writes after the caller's snapshot.
        var concurrent = original with
        {
            Value = "concurrent",
            CreatedAt = now.AddSeconds(1),
        };
        var interleaving = InterleavingDatabase.Create(
            Fixture.ConnectionMultiplexer.GetDatabase(),
            key,
            async () => _ = await store.SetEntryAsync(key, in concurrent, AbortToken)
        );
        using var expiring = _CreateCache(interleaving);
        var expired = original with
        {
            LogicalExpiresAt = now.AddMinutes(-1),
            PhysicalExpiresAt = now.AddMinutes(-1),
            ExpectedConcurrencyStamp = snapshot.ConcurrencyStamp,
        };

        // when
        var deleted = await ((IFactoryCacheStore)expiring).SetEntryAsync(key, in expired, AbortToken);

        // then - the delete compared against the value it would remove, so the newer value survives
        deleted.Should().BeFalse();
        (await writer.GetAsync<string>(key, AbortToken)).Value.Should().Be("concurrent");
    }

    private RedisCache _CreateCache(IDatabase database)
    {
        var options = new RedisCacheOptions
        {
            ConnectionMultiplexer = InterleavingMultiplexer.Create(Fixture.ConnectionMultiplexer, database),
            KeyPrefix = "",
        };

        return new RedisCache(
            new SystemJsonSerializer(),
            TimeProvider.System,
            options,
            Fixture.ScriptsLoader,
            LoggerFactory.CreateLogger<RedisCache>()
        );
    }
}

/// <summary>Forwards every call to a real multiplexer, but hands out the given database.</summary>
public class InterleavingMultiplexer : DispatchProxy
{
    private IConnectionMultiplexer _inner = null!;
    private IDatabase _database = null!;

    internal static IConnectionMultiplexer Create(IConnectionMultiplexer inner, IDatabase database)
    {
        var proxy = Create<IConnectionMultiplexer, InterleavingMultiplexer>();
        var self = (InterleavingMultiplexer)(object)proxy;
        self._inner = inner;
        self._database = database;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (string.Equals(targetMethod!.Name, nameof(IConnectionMultiplexer.GetDatabase), StringComparison.Ordinal))
        {
            return _database;
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw e.InnerException;
        }
    }
}

/// <summary>
/// Forwards every call to a real database and runs a concurrent write once, at the first command that reads or
/// compare-deletes the watched key: right after a GET of it returns, or right before a script runs.
/// </summary>
public class InterleavingDatabase : DispatchProxy
{
    private IDatabase _inner = null!;
    private RedisKey _key;
    private Func<Task> _concurrentWrite = null!;
    private int _fired;

    internal static IDatabase Create(IDatabase inner, RedisKey key, Func<Task> concurrentWrite)
    {
        var proxy = Create<IDatabase, InterleavingDatabase>();
        var self = (InterleavingDatabase)(object)proxy;
        self._inner = inner;
        self._key = key;
        self._concurrentWrite = concurrentWrite;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var name = targetMethod!.Name;

        if (
            string.Equals(name, nameof(IDatabase.StringGetAsync), StringComparison.Ordinal)
            && args is [RedisKey key, ..]
            && key == _key
        )
        {
            return _AfterAsync((Task<RedisValue>)_Forward(targetMethod, args)!);
        }

        if (string.Equals(name, nameof(IDatabase.ScriptEvaluateAsync), StringComparison.Ordinal))
        {
            return _BeforeAsync(() => (Task<RedisResult>)_Forward(targetMethod, args)!);
        }

        return _Forward(targetMethod, args);
    }

    private async Task<RedisValue> _AfterAsync(Task<RedisValue> read)
    {
        var value = await read.ConfigureAwait(false);
        await _FireOnceAsync().ConfigureAwait(false);
        return value;
    }

    private async Task<RedisResult> _BeforeAsync(Func<Task<RedisResult>> evaluate)
    {
        await _FireOnceAsync().ConfigureAwait(false);
        return await evaluate().ConfigureAwait(false);
    }

    private Task _FireOnceAsync()
    {
        return Interlocked.Exchange(ref _fired, 1) == 0 ? _concurrentWrite() : Task.CompletedTask;
    }

    private object? _Forward(MethodInfo method, object?[]? args)
    {
        try
        {
            return method.Invoke(_inner, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw e.InnerException;
        }
    }
}
