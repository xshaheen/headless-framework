// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.AzureServiceBus.Producer;

namespace Tests.Producer;

public record TestMessage;

public sealed class ServiceBusProducerDescriptorTests
{
    [Fact]
    public void should_create_descriptor_from_type()
    {
        // given, when
        var descriptor = new ServiceBusProducerDescriptor(typeof(TestMessage), "test-topic");

        // then
        descriptor.MessageTypeName.Should().Be(nameof(TestMessage));
        descriptor.TopicPath.Should().Be("test-topic");
        descriptor.CreateSubscription.Should().BeTrue();
    }

    [Fact]
    public void should_create_descriptor_from_type_name()
    {
        // given, when
        var descriptor = new ServiceBusProducerDescriptor("CustomTypeName", "test-topic");

        // then
        descriptor.MessageTypeName.Should().Be("CustomTypeName");
        descriptor.TopicPath.Should().Be("test-topic");
        descriptor.CreateSubscription.Should().BeTrue();
    }

    [Fact]
    public void should_create_descriptor_with_subscription_disabled()
    {
        // given, when
        var descriptor = new ServiceBusProducerDescriptor(typeof(TestMessage), "test-topic", createSubscription: false);

        // then
        descriptor.CreateSubscription.Should().BeFalse();
    }

    [Fact]
    public void should_create_generic_descriptor()
    {
        // given, when
        var descriptor = new ServiceBusProducerDescriptor<TestMessage>("test-topic");

        // then
        descriptor.MessageTypeName.Should().Be(nameof(TestMessage));
        descriptor.TopicPath.Should().Be("test-topic");
        descriptor.CreateSubscription.Should().BeTrue();
    }

    [Fact]
    public void should_create_generic_descriptor_with_subscription_disabled()
    {
        // given, when
        var descriptor = new ServiceBusProducerDescriptor<TestMessage>("test-topic", createSubscription: false);

        // then
        descriptor.CreateSubscription.Should().BeFalse();
    }

    [Fact]
    public void should_allow_updating_topic_path()
    {
        // given
        var descriptor = new ServiceBusProducerDescriptor(typeof(TestMessage), "original-topic")
        {
            // when
            TopicPath = "new-topic",
        };

        // then
        descriptor.TopicPath.Should().Be("new-topic");
    }
}
