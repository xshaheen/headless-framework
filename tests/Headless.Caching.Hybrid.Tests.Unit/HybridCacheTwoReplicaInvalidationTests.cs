// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Messaging.InMemory;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

/// <summary>
/// Two hosts on one shared in-memory transport and one shared L2 stand in for two replicas of one application. Each
/// keeps its own L1, so the invalidation consumer must run in every replica rather than in one of them (#936).
/// </summary>
public sealed class HybridCacheTwoReplicaInvalidationTests : TestBase
{
    [Fact]
    public async Task should_evict_the_l1_of_both_replicas_when_one_replica_publishes_an_invalidation()
    {
        // given - both replicas hold the key in L1
        var transport = new MemoryQueue(NullLogger<MemoryQueue>.Instance);
        using var l2 = new InMemoryCache(TimeProvider.System, new InMemoryCacheOptions());
        await using var first = await _StartReplicaAsync(transport, l2);
        await using var second = await _StartReplicaAsync(transport, l2);
        var firstCache = first.GetRequiredService<HybridCache>();
        var secondCache = second.GetRequiredService<HybridCache>();
        await firstCache.LocalCache.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);
        await secondCache.LocalCache.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);

        // when - an invalidation from a writer outside both replicas goes out through the first replica's bus
        await first
            .GetRequiredService<IBus>()
            .PublishAsync(
                new CacheInvalidationMessage { InstanceId = "external-writer", Key = "key" },
                cancellationToken: AbortToken
            );

        // then - a competing subscription would have handed the one copy to a single replica
        await _WaitUntilAsync(async () =>
            !await firstCache.LocalCache.ExistsAsync("key", AbortToken)
            && !await secondCache.LocalCache.ExistsAsync("key", AbortToken)
        );
        firstCache.InvalidateCacheCalls.Should().Be(1);
        secondCache.InvalidateCacheCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_serve_a_peer_write_instead_of_the_stale_l1_copy()
    {
        // given - both replicas read the key, so both hold it in L1
        var transport = new MemoryQueue(NullLogger<MemoryQueue>.Instance);
        using var l2 = new InMemoryCache(TimeProvider.System, new InMemoryCacheOptions());
        await using var first = await _StartReplicaAsync(transport, l2);
        await using var second = await _StartReplicaAsync(transport, l2);
        var firstCache = first.GetRequiredService<HybridCache>();
        var secondCache = second.GetRequiredService<HybridCache>();
        await firstCache.UpsertAsync("key", "old", TimeSpan.FromMinutes(5), AbortToken);
        (await secondCache.GetAsync<string>("key", AbortToken)).Value.Should().Be("old");

        // when - several writes on the first replica, so a competing subscription would miss some on the second
        await firstCache.UpsertAsync("key", "new", TimeSpan.FromMinutes(5), AbortToken);
        await firstCache.UpsertAsync("other-1", "value", TimeSpan.FromMinutes(5), AbortToken);
        await firstCache.UpsertAsync("other-2", "value", TimeSpan.FromMinutes(5), AbortToken);
        await firstCache.UpsertAsync("other-3", "value", TimeSpan.FromMinutes(5), AbortToken);

        // then - the second replica received every invalidation, and now reads the new value from L2
        await _WaitUntilAsync(() => ValueTask.FromResult(secondCache.InvalidateCacheCalls >= 4));
        (await secondCache.LocalCache.ExistsAsync("key", AbortToken)).Should().BeFalse();
        (await secondCache.GetAsync<string>("key", AbortToken)).Value.Should().Be("new");
    }

    [Fact]
    public async Task should_flush_the_l1_of_the_replica_whose_subscription_was_re_established()
    {
        // given - both replicas hold the key in L1
        var transport = new MemoryQueue(NullLogger<MemoryQueue>.Instance);
        using var l2 = new InMemoryCache(TimeProvider.System, new InMemoryCacheOptions());
        await using var first = await _StartReplicaAsync(transport, l2);
        await using var second = await _StartReplicaAsync(transport, l2);
        var firstCache = first.GetRequiredService<HybridCache>();
        var secondCache = second.GetRequiredService<HybridCache>();
        await firstCache.LocalCache.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);
        await secondCache.LocalCache.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);

        // when - a runtime subscription joins the second replica's invalidation subscription, which rebuilds that
        // subscription's clients, so it is re-established and anything published in between may be lost
        await using var handle = await second
            .GetRequiredService<IRuntimeSubscriber>()
            .SubscribeAsync<TopologyProbe>(
                static (_, _, _) => ValueTask.CompletedTask,
                new RuntimeSubscriptionOptions
                {
                    HandlerId = "tests.topology-probe",
                    Identity = HybridCacheInvalidationConsumer.Identity,
                    EveryInstance = true,
                },
                AbortToken
            );

        // then - only the replica with the gap drops its L1
        await _WaitUntilAsync(async () => !await secondCache.LocalCache.ExistsAsync("key", AbortToken));
        (await firstCache.LocalCache.ExistsAsync("key", AbortToken)).Should().BeTrue();
    }

    private async Task<ServiceProvider> _StartReplicaAsync(MemoryQueue transport, InMemoryCache sharedL2)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddHeadlessCaching(setup =>
        {
            setup.AddMemoryTier();
            setup.RegisterTierProvider(
                CacheConstants.RemoteCacheProvider,
                svc =>
                {
                    var remote = new InMemoryRemoteCacheAdapter(sharedL2);
                    svc.AddSingleton<IRemoteCache>(remote);
                    svc.AddKeyedSingleton<ICache>(CacheConstants.RemoteCacheProvider, remote);
                }
            );
            setup.UseHybrid();
        });
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
        });

        // Registered last, so both replicas resolve the one transport instead of their own.
        services.AddSingleton(transport);

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        return provider;
    }

    private static async Task _WaitUntilAsync(Func<ValueTask<bool>> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!await condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    public sealed record TopologyProbe(string Value);
}
