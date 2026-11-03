// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Two hosts, each with its own Redis connection the way two processes would have, share one Redis server as L2 and as
/// the invalidation backplane. A write on one must evict the other's L1, and a host whose subscription was cut must flush
/// its L1, because the invalidations sent during the gap never reached it.
/// </summary>
[Collection(nameof(RedisCacheFixture))]
public sealed class RedisHybridCacheBackplaneTests(RedisCacheFixture fixture) : TestBase
{
    private static readonly TimeSpan _Wait = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task should_evict_the_peer_l1_when_one_host_writes()
    {
        // given - both hosts subscribed, and the second holds the key in L1
        var prefix = $"hybrid-bp-{Faker.Random.AlphaNumeric(8)}:";
        await using var first = await _StartHostAsync(prefix);
        await using var second = await _StartHostAsync(prefix);
        var key = Faker.Random.AlphaNumeric(12);
        await first.Cache.UpsertAsync(key, "v1", TimeSpan.FromMinutes(5), AbortToken);
        // Let the first write's invalidation land before the read, so it cannot evict the copy the read caches.
        await _WaitUntilAsync(() => ValueTask.FromResult(second.Cache.InvalidateCacheCalls >= 1));
        (await second.Cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("v1");
        (await second.Cache.LocalCache.ExistsAsync(key, AbortToken)).Should().BeTrue();

        // when
        await first.Cache.UpsertAsync(key, "v2", TimeSpan.FromMinutes(5), AbortToken);

        // then - the peer drops its stale copy and reads the new value from L2; the writer ignores its own echo
        await _WaitUntilAsync(async () =>
            second.Cache.InvalidateCacheCalls >= 2 && !await second.Cache.LocalCache.ExistsAsync(key, AbortToken)
        );
        (await second.Cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("v2");
        (await first.Cache.LocalCache.GetAsync<string>(key, AbortToken)).Value.Should().Be("v2");
        first.Cache.InvalidateCacheCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_flush_the_l1_after_the_subscription_connection_is_cut_and_keep_invalidating()
    {
        // given - an L1 entry whose invalidation was lost (written to L1 only, so no peer message covers it)
        var prefix = $"hybrid-gap-{Faker.Random.AlphaNumeric(8)}:";
        await using var writer = await _StartHostAsync(prefix);
        await using var reader = await _StartHostAsync(prefix);
        var staleKey = Faker.Random.AlphaNumeric(12);
        await reader.Cache.LocalCache.UpsertAsync(staleKey, "stale", TimeSpan.FromMinutes(5), AbortToken);
        var generationBefore = reader.Backplane.EstablishedGeneration;

        // when - Redis drops every pub/sub connection, so both hosts lose their subscription until they reconnect
        var killed = await fixture
            .ConnectionMultiplexer.GetServer(fixture.ConnectionMultiplexer.GetEndPoints()[0])
            .ClientKillAsync(clientType: ClientType.PubSub);

        // then - the reader re-subscribes, then flushes the L1 entry it can no longer trust
        killed.Should().BePositive();
        await _WaitUntilAsync(() => ValueTask.FromResult(reader.Backplane.EstablishedGeneration > generationBefore));
        (await reader.Cache.LocalCache.ExistsAsync(staleKey, AbortToken)).Should().BeFalse();

        // and the new subscription carries invalidations again
        var key = Faker.Random.AlphaNumeric(12);
        await writer.Cache.UpsertAsync(key, "v1", TimeSpan.FromMinutes(5), AbortToken);
        await _WaitUntilAsync(() => ValueTask.FromResult(reader.Cache.InvalidateCacheCalls >= 1));
        (await reader.Cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("v1");
        (await reader.Cache.LocalCache.ExistsAsync(key, AbortToken)).Should().BeTrue();
        await writer.Cache.UpsertAsync(key, "v2", TimeSpan.FromMinutes(5), AbortToken);
        await _WaitUntilAsync(async () =>
            reader.Cache.InvalidateCacheCalls >= 2 && !await reader.Cache.LocalCache.ExistsAsync(key, AbortToken)
        );
        (await reader.Cache.GetAsync<string>(key, AbortToken)).Value.Should().Be("v2");
    }

    private async Task<BackplaneHost> _StartHostAsync(string keyPrefix)
    {
        var connection = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHeadlessCaching(setup =>
        {
            setup.AddMemoryTier();
            setup.AddRedisTier(options =>
            {
                options.ConnectionMultiplexer = connection;
                options.KeyPrefix = keyPrefix;
            });
            setup.UseHybrid();
            setup.UseRedisBackplane();
        });

        var host = builder.Build();
        await host.StartAsync(AbortToken);

        var cache = (HybridCache)host.Services.GetRequiredService<ICache>();
        var backplane = (RedisHybridCacheBackplane)host.Services.GetRequiredService<IHybridCacheBackplane>();

        // The subscription is made in the background; until it is, a peer's invalidation would not arrive.
        await _WaitUntilAsync(() => ValueTask.FromResult(backplane.EstablishedGeneration >= 1));

        return new BackplaneHost(host, connection, cache, backplane);
    }

    private static async Task _WaitUntilAsync(Func<ValueTask<bool>> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(_Wait);

        while (!await condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed record BackplaneHost(
        IHost Host,
        ConnectionMultiplexer Connection,
        HybridCache Cache,
        RedisHybridCacheBackplane Backplane
    ) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Host.StopAsync(CancellationToken.None);

            if (Host is IAsyncDisposable asyncHost)
            {
                await asyncHost.DisposeAsync();
            }
            else
            {
                Host.Dispose();
            }

            await Connection.DisposeAsync();
        }
    }
}
