// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Registration;

public sealed class ProviderConfigBagTests
{
    [Fact]
    public void should_store_message_scope_provider_config()
    {
        // given
        var builder = new MessageBuilder<TestMessage>(new ServiceCollection());
        var config = new FakeProviderConfig("message");

        // when
        ((IMessageProviderConfigBuilder<TestMessage>)builder).SetMessageProviderConfig(config);
        var registration = builder.Build();

        // then
        registration.ProviderConfigs.Should().ContainKey(typeof(FakeProviderConfig));
        registration.ProviderConfigs[typeof(FakeProviderConfig)].Should().Be(config);
    }

    [Fact]
    public void should_replace_config_of_same_type_in_one_scope()
    {
        // given
        var builder = new MessageBuilder<TestMessage>(new ServiceCollection());
        var first = new FakeProviderConfig("first");
        var second = new FakeProviderConfig("second");

        // when
        ((IMessageProviderConfigBuilder<TestMessage>)builder).SetMessageProviderConfig(first);
        ((IMessageProviderConfigBuilder<TestMessage>)builder).SetMessageProviderConfig(second);
        var registration = builder.Build();

        // then
        registration.ProviderConfigs.Should().ContainSingle();
        registration.ProviderConfigs[typeof(FakeProviderConfig)].Should().Be(second);
    }

    [Fact]
    public void should_overlay_consumer_config_over_message_config()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessageBuilder<TestMessage>(services);
        var messageConfig = new FakeProviderConfig("message");
        var consumerConfig = new FakeProviderConfig("consumer");

        // when
        ((IMessageProviderConfigBuilder<TestMessage>)builder).SetMessageProviderConfig(messageConfig);
        builder.OnBus<TestConsumer>(consumer =>
            ((IConsumerProviderConfigBuilder)consumer).SetConsumerProviderConfig(consumerConfig)
        );
        var registration = builder.Build();

        // then
        var effectiveConfig = registration.Consumers.Single().ProviderConfigs[typeof(FakeProviderConfig)];
        effectiveConfig.Should().Be(consumerConfig);
    }

    [Fact]
    public void should_merge_non_overlapping_message_and_consumer_configs()
    {
        // given
        var services = new ServiceCollection();
        var builder = new MessageBuilder<TestMessage>(services);
        var messageConfig = new FakeProviderConfig("message");
        var consumerConfig = new OtherProviderConfig("consumer");

        // when
        ((IMessageProviderConfigBuilder<TestMessage>)builder).SetMessageProviderConfig(messageConfig);
        builder.OnBus<TestConsumer>(consumer =>
            ((IConsumerProviderConfigBuilder)consumer).SetConsumerProviderConfig(consumerConfig)
        );
        var registration = builder.Build();

        // then
        registration
            .Consumers.Single()
            .ProviderConfigs.Values.Should()
            .BeEquivalentTo<object>([messageConfig, consumerConfig]);
    }

    [Fact]
    public void should_build_empty_configs_when_unset()
    {
        // given
        var builder = new MessageBuilder<TestMessage>(new ServiceCollection());

        // when
        var registration = builder.Build();

        // then
        registration.ProviderConfigs.Should().BeEmpty();
        registration.Consumers.Should().BeEmpty();
    }

    private sealed record TestMessage;

    private sealed record FakeProviderConfig(string Value);

    private sealed record OtherProviderConfig(string Value);

    private sealed class TestConsumer : IConsume<TestMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<TestMessage> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
