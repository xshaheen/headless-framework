// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// The consumer only carries the bus to the listeners: every delivery reaches each listener, and every establishment
/// of the subscription has them flush L1 with its generation.
/// </summary>
public sealed class HybridCacheInvalidationConsumerTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public async Task should_hand_each_delivered_invalidation_to_every_listener()
    {
        // given
        var first = Substitute.For<IHybridCacheInvalidationListener>();
        var second = Substitute.For<IHybridCacheInvalidationListener>();
        var consumer = new HybridCacheInvalidationConsumer([first, second]);
        var message = new CacheInvalidationMessage { InstanceId = "node-a", Key = "key" };

        // when
        await consumer.ConsumeAsync(_CreateContext(message), AbortToken);

        // then
        await first.Received(1).OnInvalidationAsync(message, AbortToken);
        await second.Received(1).OnInvalidationAsync(message, AbortToken);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    public async Task should_have_every_listener_flush_on_each_establishment(bool isReconnect, long generation)
    {
        // given
        var first = Substitute.For<IHybridCacheInvalidationListener>();
        var second = Substitute.For<IHybridCacheInvalidationListener>();
        var consumer = new HybridCacheInvalidationConsumer([first, second]);

        // when
        await consumer.OnSubscriptionEstablishedAsync(
            new SubscriptionEstablishedContext(
                HybridCacheInvalidationConsumer.Identity,
                [CacheInvalidationMessage.MessageName],
                isReconnect,
                generation
            ),
            AbortToken
        );

        // then
        await first.Received(1).OnSubscriptionEstablishedAsync(generation, AbortToken);
        await second.Received(1).OnSubscriptionEstablishedAsync(generation, AbortToken);
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
            MessageName = CacheInvalidationMessage.MessageName,
        };
    }
}
