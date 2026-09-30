// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HybridCacheInvalidationConsumerTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public async Task should_route_unnamed_message_to_default_hybrid()
    {
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        var cache = new HybridCache(
            l1,
            new InMemoryRemoteCacheAdapter(l2),
            Substitute.For<IBus>(),
            new HybridCacheOptions { InstanceId = "node-b" },
            timeProvider: _timeProvider
        );
        await using var _ = cache;
        const string key = "key";
        const string otherKey = "other-key";
        await l1.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);
        await l1.UpsertAsync(otherKey, "other-value", TimeSpan.FromMinutes(5), AbortToken);
        await l2.UpsertAsync(key, "value", TimeSpan.FromMinutes(5), AbortToken);
        var provider = Substitute.For<ICacheProvider>();
        provider.GetCacheOrNull(CacheConstants.HybridCacheProvider).Returns(cache);
        var consumer = _CreateConsumer(provider);

        await consumer.ConsumeAsync(
            _CreateContext(new CacheInvalidationMessage { InstanceId = "node-a", Key = key }),
            AbortToken
        );

        (await l1.ExistsAsync(key, AbortToken)).Should().BeFalse();
        (await l1.ExistsAsync(otherKey, AbortToken)).Should().BeTrue();
        (await l2.ExistsAsync(key, AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task should_ignore_message_when_target_cache_is_not_registered()
    {
        var provider = Substitute.For<ICacheProvider>();
        provider.GetCacheOrNull("missing").Returns((ICache?)null);
        var consumer = _CreateConsumer(provider);

        var act = async () =>
            await consumer.ConsumeAsync(
                _CreateContext(
                    new CacheInvalidationMessage
                    {
                        InstanceId = "node-a",
                        CacheName = "missing",
                        Key = "key",
                    }
                ),
                AbortToken
            );

        await act.Should().NotThrowAsync();
        provider.Received(1).GetCacheOrNull("missing");
    }

    [Fact]
    public async Task should_swallow_non_cancellation_failure_at_consumer_boundary()
    {
        var provider = Substitute.For<ICacheProvider>();
        provider
            .GetCacheOrNull(CacheConstants.HybridCacheProvider)
            .Returns(_ => throw new InvalidOperationException("provider failed"));
        var consumer = _CreateConsumer(provider);

        var act = async () =>
            await consumer.ConsumeAsync(
                _CreateContext(new CacheInvalidationMessage { InstanceId = "node-a", Key = "key" }),
                AbortToken
            );

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_propagate_caller_cancellation_from_consumer_boundary()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var provider = Substitute.For<ICacheProvider>();
        provider
            .GetCacheOrNull(CacheConstants.HybridCacheProvider)
            .Returns(_ => throw new OperationCanceledException(cancellation.Token));
        var consumer = _CreateConsumer(provider);

        var act = async () =>
            await consumer.ConsumeAsync(
                _CreateContext(new CacheInvalidationMessage { InstanceId = "node-a", Key = "key" }),
                cancellation.Token
            );

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_flush_the_l1_of_the_default_and_every_named_hybrid_on_a_reconnect()
    {
        // given - two hybrids, each holding an L1 entry that an invalidation during the gap may have made stale
        using var defaultL1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        using var namedL1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        await using var defaultCache = _CreateHybrid(defaultL1, l2);
        await using var namedCache = _CreateHybrid(namedL1, l2);
        await defaultL1.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);
        await namedL1.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);
        await l2.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);
        var provider = Substitute.For<ICacheProvider>();
        provider.GetCacheOrNull(CacheConstants.HybridCacheProvider).Returns(defaultCache);
        provider.RegisteredNames.Returns(new HashSet<string>(StringComparer.Ordinal) { "tenant", "plain" });
        provider.GetCacheOrNull("tenant").Returns(namedCache);
        provider.GetCacheOrNull("plain").Returns(Substitute.For<ICache>());

        // when
        await _CreateConsumer(provider).OnSubscriptionEstablishedAsync(_Established(isReconnect: true), AbortToken);

        // then - both L1s are empty, while L2 keeps the entry as the source of truth
        (await defaultL1.ExistsAsync("key", AbortToken))
            .Should()
            .BeFalse();
        (await namedL1.ExistsAsync("key", AbortToken)).Should().BeFalse();
        (await l2.ExistsAsync("key", AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task should_keep_the_l1_on_the_first_establishment()
    {
        // given
        using var l1 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        using var l2 = new InMemoryCache(_timeProvider, new InMemoryCacheOptions());
        await using var cache = _CreateHybrid(l1, l2);
        await l1.UpsertAsync("key", "value", TimeSpan.FromMinutes(5), AbortToken);
        var provider = Substitute.For<ICacheProvider>();
        provider.GetCacheOrNull(CacheConstants.HybridCacheProvider).Returns(cache);

        // when
        await _CreateConsumer(provider).OnSubscriptionEstablishedAsync(_Established(isReconnect: false), AbortToken);

        // then - nothing was missed before the first subscription, so nothing is flushed
        (await l1.ExistsAsync("key", AbortToken))
            .Should()
            .BeTrue();
        provider.DidNotReceive().GetCacheOrNull(Arg.Any<string>());
    }

    private HybridCache _CreateHybrid(InMemoryCache l1, InMemoryCache l2) =>
        new(
            l1,
            new InMemoryRemoteCacheAdapter(l2),
            Substitute.For<IBus>(),
            new HybridCacheOptions { InstanceId = Faker.Random.AlphaNumeric(8) },
            timeProvider: _timeProvider
        );

    private static SubscriptionEstablishedContext _Established(bool isReconnect) =>
        new(
            HybridCacheInvalidationConsumer.Identity,
            [nameof(CacheInvalidationMessage)],
            isReconnect,
            Generation: isReconnect ? 2 : 1
        );

    private static HybridCacheInvalidationConsumer _CreateConsumer(ICacheProvider provider)
    {
        return new(provider, NullLogger<HybridCacheInvalidationConsumer>.Instance);
    }

    private ConsumeContext<CacheInvalidationMessage> _CreateContext(CacheInvalidationMessage message)
    {
        return new()
        {
            Lane = MessageLane.Bus,
            Message = message,
            MessageId = Faker.Random.Guid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = _timeProvider.GetUtcNow(),
            MessageName = nameof(CacheInvalidationMessage),
        };
    }
}
