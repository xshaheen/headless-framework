// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;

namespace Tests.Internal;

public sealed class ConsumerRegistryExtensionsTests
{
    [Fact]
    public void should_return_null_when_no_consumer_has_the_requested_config_type()
    {
        // given — consumer in the group has no provider config of the requested type
        var registry = new ConsumerRegistry();
        registry.Register(_Metadata("orders", IntentType.Bus, "order.created", providerConfigs: []));

        // when
        var result = registry.ResolveConsumerConfig<FakeConsumerConfig>("orders", IntentType.Bus);

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
                IntentType.Bus,
                "order.created",
                new Dictionary<Type, object> { [typeof(FakeConsumerConfig)] = config }
            )
        );

        // when
        var result = registry.ResolveConsumerConfig<FakeConsumerConfig>("orders", IntentType.Bus);

        // then
        result.Should().Be(config);
    }

    [Fact]
    public void should_return_config_when_multiple_consumers_in_group_share_identical_config()
    {
        // given — two message types in the same consumer group, both with the same record config
        // (record value equality → Distinct deduplicates to one)
        var config = new FakeConsumerConfig("value-a");
        var registry = new ConsumerRegistry();
        registry.Register(
            _Metadata(
                "orders",
                IntentType.Bus,
                "order.created",
                new Dictionary<Type, object> { [typeof(FakeConsumerConfig)] = config }
            )
        );
        registry.Register(
            _Metadata(
                "orders",
                IntentType.Bus,
                "order.shipped",
                new Dictionary<Type, object> { [typeof(FakeConsumerConfig)] = config }
            )
        );

        // when
        var result = registry.ResolveConsumerConfig<FakeConsumerConfig>("orders", IntentType.Bus);

        // then
        result.Should().Be(config);
    }

    [Fact]
    public void should_throw_when_multiple_consumers_in_group_have_conflicting_configs()
    {
        // given — two message types in the same consumer group, but with different configs
        var registry = new ConsumerRegistry();
        registry.Register(
            _Metadata(
                "orders",
                IntentType.Bus,
                "order.created",
                new Dictionary<Type, object> { [typeof(FakeConsumerConfig)] = new FakeConsumerConfig("value-a") }
            )
        );
        registry.Register(
            _Metadata(
                "orders",
                IntentType.Bus,
                "order.shipped",
                new Dictionary<Type, object> { [typeof(FakeConsumerConfig)] = new FakeConsumerConfig("value-b") }
            )
        );

        // when
        var act = () => registry.ResolveConsumerConfig<FakeConsumerConfig>("orders", IntentType.Bus);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*conflicting*");
    }

    [Fact]
    public void should_ignore_consumers_in_a_different_group()
    {
        // given — config is in "logistics" group, not "orders"
        var config = new FakeConsumerConfig("value-a");
        var registry = new ConsumerRegistry();
        registry.Register(
            _Metadata(
                "logistics",
                IntentType.Bus,
                "order.created",
                new Dictionary<Type, object> { [typeof(FakeConsumerConfig)] = config }
            )
        );

        // when
        var result = registry.ResolveConsumerConfig<FakeConsumerConfig>("orders", IntentType.Bus);

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
                IntentType.Queue,
                "order.created",
                new Dictionary<Type, object> { [typeof(FakeConsumerConfig)] = config }
            )
        );

        // when
        var result = registry.ResolveConsumerConfig<FakeConsumerConfig>("orders", IntentType.Bus);

        // then
        result.Should().BeNull();
    }

    private static ConsumerMetadata _Metadata(
        string group,
        IntentType intentType,
        string messageName,
        Dictionary<Type, object> providerConfigs
    ) =>
        new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), messageName, group, 1, intentType)
        {
            ProviderConfigs = providerConfigs,
        };

    private sealed record FakeConsumerConfig(string Value);

    private sealed record TestMessage;

    private sealed class TestConsumer : IConsume<TestMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<TestMessage> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
