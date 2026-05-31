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
            IntentType: IntentType.Bus
        );

        // then
        metadata.MessageType.Should().Be(messageType);
        metadata.ConsumerType.Should().Be(consumerType);
        metadata.MessageName.Should().Be(messageName);
        metadata.Group.Should().Be(group);
        metadata.Concurrency.Should().Be(concurrency);
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
            IntentType: IntentType.Bus
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
            IntentType: IntentType.Bus
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
            IntentType: IntentType.Bus
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
            IntentType: IntentType.Bus
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
            IntentType: IntentType.Bus
        );
        var metadata2 = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "messageName",
            "group",
            5,
            IntentType: IntentType.Bus
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
            IntentType: IntentType.Bus
        );
        var metadata2 = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "different-messageName",
            "group",
            5,
            IntentType: IntentType.Bus
        );

        // then
        metadata1.Should().NotBe(metadata2);
        (metadata1 != metadata2).Should().BeTrue();
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
