// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Registration;
using Microsoft.Extensions.Options;

namespace Tests.Internal;

public sealed class ProviderHeaderContributionTests
{
    private const string _ProviderHeader = "x-provider-key";
    private const string _OtherProviderHeader = "x-other-provider-key";

    [Fact]
    public void should_stamp_header_value_from_provider_contribution()
    {
        // given
        var factory = _CreateFactory(new FakeProviderConfig(_ProviderHeader, static message => message.Key));

        // when
        var prepared = factory.Create(new TestMessage("tenant-1"));

        // then
        prepared.Message.Headers[_ProviderHeader].Should().Be("tenant-1");
    }

    [Fact]
    public void should_apply_multiple_provider_contributions()
    {
        // given
        var factory = _CreateFactory(
            new FakeProviderConfig(_ProviderHeader, static message => message.Key),
            new OtherProviderConfig(_OtherProviderHeader, static message => $"other-{message.Key}")
        );

        // when
        var prepared = factory.Create(new TestMessage("tenant-1"));

        // then
        prepared.Message.Headers[_ProviderHeader].Should().Be("tenant-1");
        prepared.Message.Headers[_OtherProviderHeader].Should().Be("other-tenant-1");
    }

    [Fact]
    public void should_reject_reserved_contribution_header()
    {
        // given
        var factory = _CreateFactory(new FakeProviderConfig(Headers.MessageId, static message => message.Key));

        // when
        var act = () => factory.Create(new TestMessage("tenant-1"));

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*reserved header*headless-msg-id*");
    }

    [Fact]
    public void should_reject_contribution_header_names_with_control_characters()
    {
        // given
        var factory = _CreateFactory(new FakeProviderConfig("x-provider\r\nkey", static message => message.Key));

        // when
        var act = () => factory.Create(new TestMessage("tenant-1"));

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot contain control characters*");
    }

    [Fact]
    public void should_reject_tenant_id_contribution_header()
    {
        // given
        var factory = _CreateFactory(new FakeProviderConfig(Headers.TenantId, static message => message.Key));

        // when
        var act = () => factory.Create(new TestMessage("tenant-1"));

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*reserved header*headless-tenant-id*");
    }

    [Fact]
    public void should_reject_traceparent_contribution_header()
    {
        // given
        var factory = _CreateFactory(new FakeProviderConfig(Headers.TraceParent, static message => message.Key));

        // when
        var act = () => factory.Create(new TestMessage("tenant-1"));

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*reserved header*traceparent*");
    }

    [Fact]
    public void should_reject_contribution_value_with_control_characters()
    {
        // given
        var factory = _CreateFactory(new FakeProviderConfig(_ProviderHeader, static _ => "bad\r\nvalue"));

        // when
        var act = () => factory.Create(new TestMessage("tenant-1"));

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*control characters*");
    }

    [Fact]
    public void should_skip_contribution_for_null_payload()
    {
        // given
        var invoked = false;
        var factory = _CreateFactory(
            new FakeProviderConfig(
                _ProviderHeader,
                message =>
                {
                    invoked = true;
                    return message.Key;
                }
            )
        );

        // when
        var prepared = factory.Create<TestMessage>(null);

        // then
        invoked.Should().BeFalse();
        prepared.Message.Headers.Should().NotContainKey(_ProviderHeader);
    }

    [Fact]
    public void should_noop_when_no_provider_config_is_registered()
    {
        // given
        var factory = _CreateFactory();

        // when
        var prepared = factory.Create(new TestMessage("tenant-1"));

        // then
        prepared.Message.Headers.Should().NotContainKey(_ProviderHeader);
    }

    [Fact]
    public void should_not_apply_consumer_side_provider_config_at_publish()
    {
        // given
        var consumerConfig = new FakeProviderConfig(_ProviderHeader, static message => message.Key);
        var registration = new MessageRegistration(
            typeof(TestMessage),
            null,
            null,
            new Dictionary<Type, object>(),
            [
                new MessageConsumerRegistration(
                    typeof(TestConsumer),
                    IntentType.Bus,
                    IsAssemblyScan: false,
                    Group: null,
                    Concurrency: 1,
                    HandlerId: null,
                    CircuitBreakerOverride: null,
                    ProviderConfigs: _Configs(consumerConfig)
                ),
            ]
        );
        var factory = _CreateFactory(registration);

        // when
        var prepared = factory.Create(new TestMessage("tenant-1"));

        // then
        prepared.Message.Headers.Should().NotContainKey(_ProviderHeader);
    }

    private static MessagePublishRequestFactory _CreateFactory(params object[] providerConfigs) =>
        _CreateFactory(
            new MessageRegistration(
                typeof(TestMessage),
                null,
                null,
                providerConfigs.ToDictionary(static config => config.GetType(), static config => config),
                []
            )
        );

    private static MessagePublishRequestFactory _CreateFactory(MessageRegistration registration)
    {
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(TestMessage), "test.message");

        return new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            Options.Create(new MessagingOptions()),
            registry,
            new NullCurrentTenant(),
            new MessageMetadataRegistry([registration])
        );
    }

    private static IReadOnlyDictionary<Type, object> _Configs(object config) =>
        new Dictionary<Type, object> { [config.GetType()] = config };

    private sealed record TestMessage(string Key);

    private sealed class TestConsumer : IConsume<TestMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<TestMessage> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class FakeProviderConfig(string headerName, Func<TestMessage, string?> selector)
        : IProviderHeaderContributions
    {
        public IReadOnlyList<ProviderHeaderContribution> HeaderContributions { get; } =
        [new ProviderHeaderContribution(headerName, message => selector((TestMessage)message))];
    }

    private sealed class OtherProviderConfig(string headerName, Func<TestMessage, string?> selector)
        : IProviderHeaderContributions
    {
        public IReadOnlyList<ProviderHeaderContribution> HeaderContributions { get; } =
        [new ProviderHeaderContribution(headerName, message => selector((TestMessage)message))];
    }
}
