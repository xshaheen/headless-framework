// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class ConsumerMetadataTests : TestBase
{
    [Fact]
    public void should_create_metadata_with_all_properties()
    {
        // given
        var messageType = typeof(MetadataTestMessage);
        var consumerType = typeof(MetadataTestConsumer);
        const string messageName = "test.messageName";
        const byte concurrency = 5;

        // when
        var metadata = new ConsumerMetadata(
            messageType,
            consumerType,
            messageName,
            concurrency,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "test-consumer",
            MessageContractVersion: "v1"
        );

        // then
        metadata.MessageType.Should().Be(messageType);
        metadata.ConsumerType.Should().Be(consumerType);
        metadata.MessageName.Should().Be(messageName);
        metadata.Concurrency.Should().Be(concurrency);
        metadata.ConsumerIdentity.Should().Be("test-consumer");
        metadata.MessageContractVersion.Should().Be("v1");
    }

    [Fact]
    public void should_subscribe_a_bus_consumer_under_its_identity()
    {
        // when
        var metadata = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "test.messageName",
            1,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.metadata.bus-subscription",
            MessageContractVersion: "v1"
        );

        // then
        metadata.SubscriptionName.Should().Be("tests.metadata.bus-subscription");
    }

    [Fact]
    public void should_subscribe_a_queue_consumer_under_its_message_name()
    {
        // when
        var metadata = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "test.messageName",
            1,
            Lane: MessageLane.Queue,
            ConsumerIdentity: "tests.metadata.queue-subscription",
            MessageContractVersion: "v1"
        );

        // then
        metadata.SubscriptionName.Should().Be("test.messageName");
    }

    [Fact]
    public void should_support_with_expression_for_topic()
    {
        // given
        var original = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "original.messageName",
            1,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.metadata.topic",
            MessageContractVersion: "v1"
        );

        // when
        var updated = original with
        {
            MessageName = "new.messageName",
        };

        // then
        updated.MessageName.Should().Be("new.messageName");
        updated.MessageType.Should().Be(original.MessageType);
        updated.ConsumerType.Should().Be(original.ConsumerType);
        updated.SubscriptionName.Should().Be(original.SubscriptionName);
        updated.Concurrency.Should().Be(original.Concurrency);
    }

    [Fact]
    public void should_support_with_expression_for_concurrency()
    {
        // given
        var original = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "messageName",
            1,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.metadata.concurrency",
            MessageContractVersion: "v1"
        );

        // when
        var updated = original with
        {
            Concurrency = 10,
        };

        // then
        updated.Concurrency.Should().Be(10);
    }

    [Fact]
    public void should_support_record_equality()
    {
        // given
        var metadata1 = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "messageName",
            5,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.metadata.equality",
            MessageContractVersion: "v1"
        );
        var metadata2 = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "messageName",
            5,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.metadata.equality",
            MessageContractVersion: "v1"
        );

        // then
        metadata1.Should().Be(metadata2);
        (metadata1 == metadata2).Should().BeTrue();
    }

    [Fact]
    public void should_not_be_equal_when_properties_differ()
    {
        // given
        var metadata1 = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "messageName",
            5,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.metadata.difference",
            MessageContractVersion: "v1"
        );
        var metadata2 = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "different-messageName",
            5,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.metadata.difference",
            MessageContractVersion: "v1"
        );

        // then
        metadata1.Should().NotBe(metadata2);
        (metadata1 != metadata2).Should().BeTrue();
    }

    [Fact]
    public void durable_identity_is_independent_from_consumer_class_and_message_name()
    {
        var original = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "orders.placed",
            1,
            MessageLane.Bus,
            ConsumerIdentity: "orders-projection",
            MessageContractVersion: "v3"
        );

        var refactored = original with
        {
            ConsumerType = typeof(RefactoredMetadataTestConsumer),
            MessageName = "orders.v2.placed",
        };

        refactored.ConsumerIdentity.Should().Be("orders-projection");
        refactored.MessageContractVersion.Should().Be("v3");
    }
}

public sealed record MetadataTestMessage(string Value);

public sealed class MetadataTestConsumer : IConsume<MetadataTestMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<MetadataTestMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class RefactoredMetadataTestConsumer : IConsume<MetadataTestMessage>
{
    public ValueTask ConsumeAsync(ConsumeContext<MetadataTestMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
