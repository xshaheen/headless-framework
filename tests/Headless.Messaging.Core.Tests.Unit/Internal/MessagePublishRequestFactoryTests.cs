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
