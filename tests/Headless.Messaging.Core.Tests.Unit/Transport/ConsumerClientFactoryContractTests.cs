// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Transport;

namespace Tests.Transport;

public sealed class ConsumerClientFactoryContractTests
{
    [Fact]
    public void should_require_explicit_lane_for_every_consumer_client()
    {
        // when
        var createMethods = typeof(IConsumerClientFactory)
            .GetMethods()
            .Where(method =>
                string.Equals(method.Name, nameof(IConsumerClientFactory.CreateAsync), StringComparison.Ordinal)
            )
            .ToArray();

        // then
        var create = createMethods.Should().ContainSingle().Subject;
        var parameters = create.GetParameters();
        parameters
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Equal(typeof(string), typeof(byte), typeof(MessageLane), typeof(CancellationToken));
        parameters[2].HasDefaultValue.Should().BeFalse("lane selection must never fall back to Bus");
    }

    [Fact]
    public void should_not_expose_the_legacy_optional_intent_factory()
    {
        // when
        var legacyType = typeof(IConsumerClientFactory)
            .Assembly.GetTypes()
            .SingleOrDefault(type =>
                string.Equals(
                    type.FullName,
                    "Headless.Messaging.Transport.IIntentAwareConsumerClientFactory",
                    StringComparison.Ordinal
                )
            );

        // then
        legacyType.Should().BeNull("all factories must require a lane through the primary contract");
    }
}
