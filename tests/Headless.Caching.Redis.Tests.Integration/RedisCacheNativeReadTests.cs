// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Caching;
using Headless.Redis;
using Headless.Serializer;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisCacheNativeReadTests : TestBase
{
    private const string _Counter = "counter";
    private static readonly TimeSpan _Remaining = TimeSpan.FromMinutes(2);
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();

    public RedisCacheNativeReadTests()
    {
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_database);
        _multiplexer.GetEndPoints(Arg.Any<bool>()).Returns([]);

        // The first fetch sees the counter before an increment. The second fetch sees the increment
        // in the same generation, paired atomically with the sidecar for the value it returned.
        _database
            .StringGetLeaseAsync(_Counter, Arg.Any<CommandFlags>())
            .Returns(_ =>
            {
                var lease = Lease<byte>.Create(1, clear: false);
                "1"u8.CopyTo(lease.Span);
                return Task.FromResult<Lease<byte>?>(lease);
            });
        _database
            .StringGetAsync(Arg.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0] == _Counter), CommandFlags.None)
            .Returns(Task.FromResult<RedisValue[]>(["1"]));
        _database
            .StringGetAsync(
                Arg.Is<RedisKey[]>(keys => keys.Length == 2 && keys[0] != _Counter),
                CommandFlags.DemandMaster
            )
            .Returns(Task.FromResult<RedisValue[]>(["100", "100"]));
        _database
            .StringGetAsync(Arg.Is<RedisKey[]>(keys => keys.Length == 2 && keys[0] == _Counter), CommandFlags.None)
            .Returns(Task.FromResult<RedisValue[]>(["2", "100"]));

        _database.KeyTimeToLiveAsync(_Counter).Returns(Task.FromResult<TimeSpan?>(_Remaining));
        var batch = Substitute.For<IBatch>();
        _database.CreateBatch(Arg.Any<object?>()).Returns(batch);
        batch.KeyTimeToLiveAsync(_Counter).Returns(Task.FromResult<TimeSpan?>(_Remaining));
    }

    [Fact]
    public async Task should_read_the_current_native_value_when_the_counter_changes_between_single_reads()
    {
        using var scripts = new HeadlessRedisScriptsLoader(_multiplexer);
        using var cache = _CreateCache(scripts);

        (await cache.GetAsync<long>(_Counter, AbortToken)).Value.Should().Be(2L);
        (await cache.ExistsAsync(_Counter, AbortToken)).Should().BeTrue();
        (await cache.GetExpirationAsync(_Counter, AbortToken)).Should().Be(_Remaining);
        var withExpiration = await cache.GetWithExpirationAsync<long>(_Counter, AbortToken);
        withExpiration.Value.Value.Should().Be(2L);
        withExpiration.Expiration.Should().Be(_Remaining);

        var buffer = new ArrayBufferWriter<byte>();
        (await cache.TryGetToAsync(_Counter, buffer, AbortToken)).Should().BeTrue();
        buffer.WrittenSpan.SequenceEqual("2"u8).Should().BeTrue();

        var entry = await ((IFactoryCacheStore)cache).TryGetEntryAsync<long>(_Counter, cancellationToken: AbortToken);
        entry.Value.Should().Be(2L);
        entry.ConcurrencyStamp.Should().Be("b64:Mg==");
    }

    [Fact]
    public async Task should_read_the_current_native_value_when_the_counter_changes_between_bulk_reads()
    {
        using var scripts = new HeadlessRedisScriptsLoader(_multiplexer);
        using var cache = _CreateCache(scripts);

        var values = await cache.GetAllAsync<long>([_Counter], AbortToken);
        values[_Counter].Value.Should().Be(2L);
        var withExpiration = await cache.GetAllWithExpirationAsync<long>([_Counter], AbortToken);
        withExpiration[_Counter].Value.Value.Should().Be(2L);
        withExpiration[_Counter].Expiration.Should().Be(_Remaining);

        var entries = await ((IFactoryCacheStore)cache).TryGetAllEntriesAsync<long>(
            [_Counter],
            cancellationToken: AbortToken
        );
        entries[0].Value.Should().Be(2L);
        entries[0].ConcurrencyStamp.Should().Be("b64:Mg==");
    }

    private RedisCache _CreateCache(HeadlessRedisScriptsLoader scripts)
    {
        return new RedisCache(
            new SystemJsonSerializer(),
            new FakeTimeProvider(),
            new RedisCacheOptions { ConnectionMultiplexer = _multiplexer },
            scripts
        );
    }
}
