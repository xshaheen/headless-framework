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
    [InlineData(IntentType.Bus, "Bus")]
    [InlineData(IntentType.Queue, "Queue")]
    public void should_preserve_legacy_intent_header(IntentType intentType, string wireValue)
    {
        // given
        var factory = _CreateFactory();

        // when
        var prepared = factory.Create(new CallbackResponse("accepted"), intentType: intentType);

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
