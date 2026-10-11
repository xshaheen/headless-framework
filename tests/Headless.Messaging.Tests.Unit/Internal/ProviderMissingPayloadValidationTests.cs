// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests.Internal;

public sealed class ProviderMissingPayloadValidationTests : TestBase
{
    [Fact]
    public void should_validate_a_null_payload_with_the_resolved_message_name_and_lane()
    {
        // given
        var config = new RecordingConfig();
        var factory = _CreateFactory(MessageLane.Queue, config);

        // when
        factory.Create<TestMessage>(null, lane: MessageLane.Queue);

        // then
        config.Calls.Should().ContainSingle().Which.Should().Be(("test.message", MessageLane.Queue));
    }

    [Fact]
    public void should_refuse_a_null_payload_when_the_provider_config_throws()
    {
        // given
        var factory = _CreateFactory(MessageLane.Bus, new RefusingConfig());

        // when
        var act = () => factory.Create<TestMessage>(null);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("refused test.message on Bus");
    }

    [Fact]
    public void should_not_validate_a_message_that_has_a_payload()
    {
        // given
        var config = new RecordingConfig();
        var factory = _CreateFactory(MessageLane.Bus, config);

        // when
        factory.Create(new TestMessage("k"));

        // then
        config.Calls.Should().BeEmpty();
    }

    private static MessagePublishRequestFactory _CreateFactory(MessageLane lane, object providerConfig)
    {
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(TestMessage), "test.message");
        var registration = new MessageRegistration(
            typeof(TestMessage),
            lane,
            null,
            null,
            new Dictionary<Type, object> { [providerConfig.GetType()] = providerConfig },
            []
        );

        return new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            Options.Create(new MessagingOptions()),
            registry,
            new NullCurrentTenant(),
            new MessageMetadataRegistry([registration], registry)
        );
    }

    private sealed record TestMessage(string Key);

    private sealed class RecordingConfig : IProviderMissingPayloadValidation
    {
        public List<(string MessageName, MessageLane Lane)> Calls { get; } = [];

        public void ValidateMissingPayload(string messageName, MessageLane lane) => Calls.Add((messageName, lane));
    }

    private sealed class RefusingConfig : IProviderMissingPayloadValidation
    {
        public void ValidateMissingPayload(string messageName, MessageLane lane) =>
            throw new InvalidOperationException($"refused {messageName} on {lane}");
    }
}
