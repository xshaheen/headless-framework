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
        var topic = "test.topic";
        var group = "test-group";
        byte concurrency = 5;

        // when
        var metadata = new ConsumerMetadata(messageType, consumerType, topic, group, concurrency);

        // then
        metadata.MessageType.Should().Be(messageType);
        metadata.ConsumerType.Should().Be(consumerType);
        metadata.Topic.Should().Be(topic);
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
            "test.topic",
            null,
            1
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
            "original.topic",
            "group",
            1
        );

        // when
        var updated = original with
        {
            Topic = "new.topic",
        };

        // then
        updated.Topic.Should().Be("new.topic");
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
            "topic",
            "original-group",
            1
        );

        // when
        var updated = original with
        {
            Group = "new-group",
        };

        // then
        updated.Group.Should().Be("new-group");
        updated.Topic.Should().Be(original.Topic);
    }

    [Fact]
    public void should_support_with_expression_for_concurrency()
    {
        // given
        var original = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "topic",
            "group",
            1
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
            "topic",
            "group",
            5
        );
        var metadata2 = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "topic",
            "group",
            5
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
            "topic",
            "group",
            5
        );
        var metadata2 = new ConsumerMetadata(
            typeof(MetadataTestMessage),
            typeof(MetadataTestConsumer),
            "different-topic",
            "group",
            5
        );

        // then
        metadata1.Should().NotBe(metadata2);
        (metadata1 != metadata2).Should().BeTrue();
    }
}

public sealed record MetadataTestMessage(string Value);

public sealed class MetadataTestConsumer : IConsume<MetadataTestMessage>
{
    public ValueTask Consume(ConsumeContext<MetadataTestMessage> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
