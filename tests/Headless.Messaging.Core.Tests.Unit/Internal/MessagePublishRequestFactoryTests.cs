// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Microsoft.Extensions.Options;

namespace Tests.Internal;

public sealed class MessagePublishRequestFactoryTests
{
    [Theory]
    [InlineData(MessageLane.Bus, "Bus")]
    [InlineData(MessageLane.Queue, "Queue")]
    public void should_preserve_legacy_intent_header(MessageLane lane, string wireValue)
    {
        // given
        var factory = _CreateFactory();

        // when
        var prepared = factory.Create(new CallbackResponse("accepted"), lane: lane);

        // then
        Headers.Intent.Should().Be("headless-intent");
        prepared.Message.Headers[Headers.Intent].Should().Be(wireValue);
    }

    [Fact]
    public void should_use_explicit_message_type_for_type_header()
    {
        // given
        var factory = _CreateFactory();
        object response = new CallbackResponse("accepted");

        // when
        var prepared = factory.Create(
            response,
            new PublishOptions { MessageName = "callbacks.messageName", MessageType = typeof(CallbackResponse) }
        );

        // then
        prepared.Message.Headers[Headers.Type].Should().Be(nameof(CallbackResponse));
    }

    [Theory]
    [InlineData(Headers.RequestedDeliveryMode)]
    [InlineData(Headers.ResolvedDeliveryMode)]
    public void should_reject_custom_delivery_metadata_headers(string header)
    {
        var factory = _CreateFactory();
        var options = new PublishOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [header] = "Durable" },
        };

        var act = () => factory.Create(new CallbackResponse("accepted"), options);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{header}*reserved*");
    }

    [Fact]
    public void should_reject_raw_routing_affinity_header()
    {
        var factory = _CreateFactory();
        var options = new PublishOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["headless-routing-affinity-key"] = "order-42",
            },
        };

        var act = () => factory.Create(new CallbackResponse("accepted"), options);

        act.Should().Throw<InvalidOperationException>().WithMessage("*headless-routing-affinity-key*reserved*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("order-42")]
    public void should_preserve_typed_affinity_and_reconcile_matching_native_header(string? raw)
    {
        var factory = _CreateAffinityFactory();
        var options = new PublishOptions
        {
            MessageName = "orders",
            RoutingAffinityKey = "order-42",
            Headers = raw is null
                ? null
                : new Dictionary<string, string?>(StringComparer.Ordinal) { ["native-key"] = raw },
        };

        var prepared = factory.Create(new CallbackResponse("accepted"), options);

        prepared.Message.Headers[Headers.RoutingAffinityKey].Should().Be("order-42");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("other")]
    public void should_reject_conflicting_native_affinity_before_preparing_message(string raw)
    {
        var factory = _CreateAffinityFactory();
        var options = new PublishOptions
        {
            MessageName = "orders",
            RoutingAffinityKey = "order-42",
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["native-key"] = raw },
        };

        var act = () => factory.Create(new CallbackResponse("accepted"), options);

        act.Should().Throw<InvalidOperationException>().WithMessage("*affinity*conflicts*native-key*");
    }

    [Fact]
    public void should_not_allow_provider_selector_to_erase_a_conflicting_raw_affinity_header()
    {
        var factory = _CreateAffinityFactory(withContributor: true);
        var options = new PublishOptions
        {
            MessageName = "orders",
            RoutingAffinityKey = "order-42",
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["native-key"] = "other" },
        };

        var act = () => factory.Create(new CallbackResponse("accepted"), options);

        act.Should().Throw<InvalidOperationException>().WithMessage("*affinity*conflicts*native-key*");
    }

    private sealed class AffinityContributor : Headless.Messaging.Registration.IProviderHeaderContributions
    {
        public IReadOnlyList<Headless.Messaging.Registration.ProviderHeaderContribution> HeaderContributions { get; } =
        [new("native-key", static _ => "order-42")];
    }

    private static MessagePublishRequestFactory _CreateAffinityFactory(bool withContributor = false)
    {
        var registration = new Headless.Messaging.Registration.MessageRegistration(
            typeof(CallbackResponse),
            MessageLane.Bus,
            "orders",
            null,
            withContributor
                ? new Dictionary<Type, object> { [typeof(AffinityContributor)] = new AffinityContributor() }
                : new Dictionary<Type, object>(),
            []
        );
        var registry = new MessageMetadataRegistry([registration]);
        var capabilities = MessagingCapabilityModel.Compose([
            MessagingProviderCapabilities.Transport(
                "Mapped",
                [MessageLane.Bus],
                true,
                [
                    new MessagingRoutingAffinityRoute(
                        MessageLane.Bus,
                        "orders",
                        new MessagingRoutingAffinityMapping("native-key")
                    ),
                ]
            ),
        ]);
        return new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            Options.Create(new MessagingOptions()),
            new ConsumerRegistry(),
            new NullCurrentTenant(),
            registry,
            capabilityGate: capabilities
        );
    }

    private static MessagePublishRequestFactory _CreateFactory()
    {
        var options = new MessagingOptions();

        return new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            Options.Create(options),
            new ConsumerRegistry(),
            new NullCurrentTenant()
        );
    }

    private sealed record CallbackResponse(string Status);
}
