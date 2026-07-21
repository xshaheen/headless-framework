// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Registration;
using Microsoft.Extensions.Options;

namespace Tests.Internal;

public sealed class MessagePublishRequestFactoryMetadataTests
{
    [Fact]
    public void should_use_resolved_metadata_type_for_default_message_name_and_type_header()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(IOrderEvent), "orders.event");
        var factory = _CreateFactory(registry, typeof(IOrderEvent));

        // when
        IOrderEvent message = new ConcreteOrderEvent("order-1");
        var prepared = factory.Create(message);

        // then
        prepared.MessageName.Should().Be("orders.event");
        prepared.Message.Headers[Headers.MessageName].Should().Be("orders.event");
        prepared.Message.Headers[Headers.Type].Should().Be(nameof(IOrderEvent));
    }

    [Fact]
    public void should_prefer_explicit_message_type_over_resolved_metadata_type()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(IOrderEvent), "orders.event");
        registry.RegisterMessageName(typeof(ConcreteOrderEvent), "orders.concrete");
        var factory = _CreateFactory(registry, typeof(IOrderEvent));

        // when
        var prepared = factory.Create(
            new ConcreteOrderEvent("order-1"),
            new PublishOptions { MessageType = typeof(ConcreteOrderEvent) }
        );

        // then
        prepared.MessageName.Should().Be("orders.concrete");
        prepared.Message.Headers[Headers.Type].Should().Be(nameof(ConcreteOrderEvent));
    }

    [Fact]
    public void should_prefer_explicit_message_name_over_resolved_metadata_name()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(IOrderEvent), "orders.event");
        var factory = _CreateFactory(registry, typeof(IOrderEvent));

        // when
        IOrderEvent message = new ConcreteOrderEvent("order-1");
        var prepared = factory.Create(message, new PublishOptions { MessageName = "orders.explicit" });

        // then
        prepared.MessageName.Should().Be("orders.explicit");
        prepared.Message.Headers[Headers.MessageName].Should().Be("orders.explicit");
        prepared.Message.Headers[Headers.Type].Should().Be(nameof(IOrderEvent));
    }

    private static MessagePublishRequestFactory _CreateFactory(ConsumerRegistry registry, Type metadataType)
    {
        var registrations = new[]
        {
            new MessageRegistration(metadataType, MessageLane.Bus, null, null, new Dictionary<Type, object>(), []),
        };

        return new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            Options.Create(new MessagingOptions()),
            registry,
            new NullCurrentTenant(),
            new MessageMetadataRegistry(registrations, registry)
        );
    }

    private interface IOrderEvent;

    private sealed record ConcreteOrderEvent(string OrderId) : IOrderEvent;
}
