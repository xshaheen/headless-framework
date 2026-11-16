// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class MethodMatcherCacheSnapshotTests : TestBase
{
    private const string _ConsumerIdentity = "snapshot-consumer";
    private const string _MessageName = "snapshot.orders";
    private const int _Invalidations = 20_000;

    [Fact]
    public async Task should_resolve_every_lookup_when_topology_is_invalidated_concurrently()
    {
        // given: a registered consumer whose topology never changes, so every lookup must find it no matter how often
        // the cache drops and rebuilds its snapshot underneath the readers.
        await using var provider = _CreateProvider();
        var cache = provider.GetRequiredService<MethodMatcherCache>();
        var subscription = new ConsumerSubscriptionKey(_ConsumerIdentity, MessageLane.Bus);

        cache.TryGetMessageNameExecutor(_MessageName, subscription, out _).Should().BeTrue();

        var misses = 0;
        var invalidating = true;

        // when: readers resolve the consumer by subscription name, by lane subscription, and by identity while a writer
        // invalidates the topology in a tight loop, as a runtime subscribe or unsubscribe does under live traffic.
        var readers = Enumerable
            .Range(0, Math.Max(2, Environment.ProcessorCount - 1))
            .Select(_ignored =>
                Task.Run(
                    () =>
                    {
                        while (Volatile.Read(ref invalidating))
                        {
                            if (
                                !cache.TryGetMessageNameExecutor(_MessageName, _ConsumerIdentity, out _)
                                || !cache.TryGetMessageNameExecutor(_MessageName, subscription, out _)
                                || !cache.TryGetConsumerIdentityExecutor(
                                    _MessageName,
                                    _ConsumerIdentity,
                                    MessageLane.Bus,
                                    out _
                                )
                                || !cache.TryGetInboxExecutor(
                                    _ConsumerIdentity,
                                    _MessageName,
                                    "1",
                                    MessageLane.Bus,
                                    out _
                                )
                                || cache.GetSubscriptionConcurrentLimit(subscription) != 3
                            )
                            {
                                Interlocked.Increment(ref misses);
                            }
                        }
                    },
                    AbortToken
                )
            )
            .ToArray();

        var writer = Task.Run(
            () =>
            {
                try
                {
                    for (var i = 0; i < _Invalidations; i++)
                    {
                        cache.Invalidate();
                    }
                }
                finally
                {
                    Volatile.Write(ref invalidating, false);
                }
            },
            AbortToken
        );

        await writer;
        await Task.WhenAll(readers);

        // then
        misses.Should().Be(0, "a lookup that overlaps an invalidation must see either the old or the new topology");
    }

    [Fact]
    public void should_keep_an_empty_topology_cached_until_invalidated()
    {
        // given
        var selector = Substitute.For<IConsumerServiceSelector>();
        selector.SelectCandidates().Returns([]);
        var cache = new MethodMatcherCache(selector);

        // when
        cache.GetAllMessageNames().Should().BeEmpty();
        cache.GetCandidatesBySubscriptionName().Should().BeEmpty();
        cache.GetSubscriptionConcurrentLimit("missing").Should().Be(1);

        // then: an empty topology is a valid snapshot, not a signal to rebuild on every lookup.
        selector.Received(1).SelectCandidates();

        cache.Invalidate();
        cache.GetAllMessageNames().Should().BeEmpty();
        selector.Received(2).SelectCandidates();
    }

    private static ServiceProvider _CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging => messaging.Message<SelectorTestMessage>(_MessageName));
        services.AddHeadlessMessaging(setup =>
        {
            setup.AddConsumer<SnapshotConsumer>();
            setup.Tune(_ConsumerIdentity, consumer => consumer.Concurrency(3));
        });
        return services.BuildServiceProvider();
    }

    [BusConsumer(_ConsumerIdentity)]
    private sealed class SnapshotConsumer : IConsume<SelectorTestMessage>
    {
        public ValueTask ConsumeAsync(
            ConsumeContext<SelectorTestMessage> context,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
    }
}
