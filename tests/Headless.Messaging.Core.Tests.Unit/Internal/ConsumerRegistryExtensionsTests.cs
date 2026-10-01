// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;

namespace Tests.Internal;

public sealed class ConsumerRegistryExtensionsTests
{
    [Fact]
    public void should_return_null_when_no_consumer_has_the_requested_config_type()
    {
        // given — consumer in the subscription has no provider config of the requested type
        var registry = new ConsumerRegistry();
        registry.Register(_Metadata("orders", MessageLane.Bus, "order.created", providerConfigs: []));

        // when
        var result = registry.ResolveConsumerConfig<FakeConsumerConfig>("orders", MessageLane.Bus);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_config_when_exactly_one_consumer_has_the_requested_config_type()
    {
        // given
        var config = new FakeConsumerConfig("value-a");
        var registry = new ConsumerRegistry();
        registry.Register(
            _Metadata(
                "orders",
                MessageLane.Bus,
                "order.created",
                new Dictionary<Type, object> { [typeof(FakeConsumerConfig)] = config }
            )
        );

        // when
        var result = registry.ResolveConsumerConfig<FakeConsumerConfig>("orders", MessageLane.Bus);

        // then
        result.Should().Be(config);
    }

    [Fact]
    public void should_return_config_when_multiple_consumers_in_subscription_share_identical_config()
    {
        // given — two message types under the same identity, both with the same record config
        // (record value equality → Distinct deduplicates to one)
        var config = new FakeConsumerConfig("value-a");
        var registry = new ConsumerRegistry();
        registry.Register(
            _Metadata(
                "orders",
                MessageLane.Bus,
                "order.created",
                new Dictionary<Type, object> { [typeof(FakeConsumerConfig)] = config }
            )
        );
        registry.Register(
            _Metadata(
                "orders",
                MessageLane.Bus,
                "order.shipped",
                new Dictionary<Type, object> { [typeof(FakeConsumerConfig)] = config }
            )
        );

        // when
        var result = registry.ResolveConsumerConfig<FakeConsumerConfig>("orders", MessageLane.Bus);

        // then
        result.Should().Be(config);
    }

    [Fact]
    public void should_throw_when_multiple_consumers_in_subscription_have_conflicting_configs()
    {
        // given — two message types under the same identity, but with different configs
        var registry = new ConsumerRegistry();
        registry.Register(
            _Metadata(
                "orders",
                MessageLane.Bus,
                "order.created",
                new Dictionary<Type, object> { [typeof(FakeConsumerConfig)] = new FakeConsumerConfig("value-a") }
            )
        );
        registry.Register(
            _Metadata(
                "orders",
                MessageLane.Bus,
                "order.shipped",
                new Dictionary<Type, object> { [typeof(FakeConsumerConfig)] = new FakeConsumerConfig("value-b") }
            )
        );

        // when
        var act = () => registry.ResolveConsumerConfig<FakeConsumerConfig>("orders", MessageLane.Bus);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*conflicting*");
    }

    [Fact]
    public void should_ignore_consumers_in_a_different_subscription()
    {
        // given — config belongs to the "logistics" subscription, not "orders"
        var config = new FakeConsumerConfig("value-a");
        var registry = new ConsumerRegistry();
        registry.Register(
            _Metadata(
                "logistics",
                MessageLane.Bus,
                "order.created",
                new Dictionary<Type, object> { [typeof(FakeConsumerConfig)] = config }
            )
        );

        // when
        var result = registry.ResolveConsumerConfig<FakeConsumerConfig>("orders", MessageLane.Bus);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_ignore_consumers_with_different_intent_type()
    {
        // given — config is registered for Queue, not Bus
        var config = new FakeConsumerConfig("value-a");
        var registry = new ConsumerRegistry();
        registry.Register(
            _Metadata(
                "orders",
                MessageLane.Queue,
                "order.created",
                new Dictionary<Type, object> { [typeof(FakeConsumerConfig)] = config }
            )
        );

        // when
        var result = registry.ResolveConsumerConfig<FakeConsumerConfig>("orders", MessageLane.Bus);

        // then
        result.Should().BeNull();
    }

    // A Bus consumer subscribes under its identity, so consumers sharing an identity share one subscription.
    private static ConsumerMetadata _Metadata(
        string identity,
        MessageLane lane,
        string messageName,
        Dictionary<Type, object> providerConfigs
    )
    {
        return new(typeof(TestMessage), typeof(TestConsumer), messageName, 1, lane, identity, "v1")
        {
            ProviderConfigs = providerConfigs,
        };
    }

    private sealed record FakeConsumerConfig(string Value);

    private sealed record TestMessage;

    private sealed class TestConsumer : IConsume<TestMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }
}
