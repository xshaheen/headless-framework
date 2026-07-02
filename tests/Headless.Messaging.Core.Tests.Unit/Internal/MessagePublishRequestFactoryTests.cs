// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Microsoft.Extensions.Options;

namespace Tests.Internal;

public sealed class MessagePublishRequestFactoryTests
{
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
