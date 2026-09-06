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
        const string group = "test-group";
        const byte concurrency = 5;

        // when
        var metadata = new ConsumerMetadata(
            messageType,
            consumerType,
            messageName,
            group,
            concurrency,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "test-consumer",
            MessageContractVersion: "v1"
        );

        // then
        metadata.MessageType.Should().Be(messageType);
        metadata.ConsumerType.Should().Be(consumerType);
        metadata.MessageName.Should().Be(messageName);
        metadata.Group.Should().Be(group);
        metadata.Concurrency.Should().Be(concurrency);
        metadata.ConsumerIdentity.Should().Be("test-consumer");
        metadata.MessageContractVersion.Should().Be("v1");
    }

    [Fact]
    public void should_allow_null_group()
    {
        // when
        var metadata = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "test.messageName",
            null,
            1,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.metadata.null-group",
            MessageContractVersion: "v1"
        );

        // then
        metadata.Group.Should().BeNull();
    }

    [Fact]
    public void should_support_with_expression_for_topic()
    {
        // given
        var original = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "original.messageName",
            "group",
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
        updated.Group.Should().Be(original.Group);
        updated.Concurrency.Should().Be(original.Concurrency);
    }

    [Fact]
    public void should_support_with_expression_for_group()
    {
        // given
        var original = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "messageName",
            "original-group",
            1,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.metadata.group",
            MessageContractVersion: "v1"
        );

        // when
        var updated = original with
        {
            Group = "new-group",
        };

        // then
        updated.Group.Should().Be("new-group");
        updated.MessageName.Should().Be(original.MessageName);
    }

    [Fact]
    public void should_support_with_expression_for_concurrency()
    {
        // given
        var original = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "messageName",
            "group",
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
            "group",
            5,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.metadata.equality",
            MessageContractVersion: "v1"
        );
        var metadata2 = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "messageName",
            "group",
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
            "group",
            5,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.metadata.difference",
            MessageContractVersion: "v1"
        );
        var metadata2 = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "different-messageName",
            "group",
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
    public void durable_identity_is_independent_from_handler_and_topology_metadata()
    {
        var original = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "orders.placed",
            "orders-primary",
            1,
            MessageLane.Bus,
            ConsumerIdentity: "orders-projection",
            MessageContractVersion: "v3",
            HandlerId: "Tests.OriginalHandler"
        );

        var refactored = original with
        {
            ConsumerType = typeof(RefactoredMetadataTestConsumer),
            MessageName = "orders.v2.placed",
            Group = "orders-refactored",
            HandlerId = "Tests.RefactoredHandler",
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
