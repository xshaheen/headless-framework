// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

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
        var first = new MessageBuilder<TestMessage>(new ServiceCollection());
        var second = new MessageBuilder<TestMessage>(new ServiceCollection());

        // when
        var firstRegistration = first.Build();
        var secondRegistration = second.Build();

        // then
        firstRegistration.ProviderConfigs.Should().BeEmpty();
        firstRegistration.ProviderConfigs.Should().BeSameAs(secondRegistration.ProviderConfigs);
        firstRegistration.Consumers.Should().BeEmpty();
    }

    [Fact]
    public void should_build_read_only_provider_config_snapshots()
    {
        // given
        var builder = new MessageBuilder<TestMessage>(new ServiceCollection());
        var config = new FakeProviderConfig("message");

        // when
        ((IMessageProviderConfigBuilder<TestMessage>)builder).SetMessageProviderConfig(config);
        var registration = builder.Build();

        // then
        registration.ProviderConfigs.Should().NotBeAssignableTo<Dictionary<Type, object>>();
    }

    [Fact]
    public void should_carry_effective_consumer_provider_config_into_consumer_metadata()
    {
        // given
        var services = new ServiceCollection();
        var messageConfig = new FakeProviderConfig("message");
        var consumerConfig = new FakeProviderConfig("consumer");

        // when
        services.AddHeadlessMessaging(setup =>
            setup.ForMessage<TestMessage>(message =>
            {
                ((IMessageProviderConfigBuilder<TestMessage>)message).SetMessageProviderConfig(messageConfig);
                message.OnBus<TestConsumer>(consumer =>
                    ((IConsumerProviderConfigBuilder)consumer).SetConsumerProviderConfig(consumerConfig)
                );
            })
        );

        using var provider = services.BuildServiceProvider();
        var metadata = provider.GetDrainedConsumerRegistry().GetAll().Single();

        // then
        metadata.ProviderConfigs[typeof(FakeProviderConfig)].Should().Be(consumerConfig);
    }

    [Fact]
    public void should_reject_duplicate_consumer_registration_with_conflicting_provider_config()
    {
        // given
        var services = new ServiceCollection();

        // when — the conflicting-config guard fires during the startup drain, so build + drain inside the act
        var action = () =>
        {
            services.AddHeadlessMessaging(setup =>
            {
                setup.ForMessage<TestMessage>(message =>
                    message.OnBus<TestConsumer>(consumer =>
                        ((IConsumerProviderConfigBuilder)consumer).SetConsumerProviderConfig(
                            new FakeProviderConfig("first")
                        )
                    )
                );
                setup.ForMessage<TestMessage>(message =>
                    message.OnBus<TestConsumer>(consumer =>
                        ((IConsumerProviderConfigBuilder)consumer).SetConsumerProviderConfig(
                            new FakeProviderConfig("second")
                        )
                    )
                );
            });
            using var provider = services.BuildServiceProvider();
            provider.GetDrainedConsumerRegistry().GetAll();
        };

        // then
        action.Should().Throw<InvalidOperationException>().WithMessage("*conflicting settings*");
    }

    [Fact]
    public void should_allow_idempotent_re_registration_when_message_config_is_class_based()
    {
        // given — class-based IProviderHeaderContributions configs hold a Func and have no
        // value equality; two distinct instances of the same type for the same consumer must
        // not be treated as a conflict during idempotent re-registration.
        var services = new ServiceCollection();

        // when — register the same message-level hatch config twice (simulates idempotent bootstrap)
        var action = () =>
            services.AddHeadlessMessaging(setup =>
            {
                setup.ForMessage<TestMessage>(message =>
                    ((IMessageProviderConfigBuilder<TestMessage>)message).SetMessageProviderConfig(
                        new ClassBasedProviderConfig(static _ => "shard-1")
                    )
                );
                setup.ForMessage<TestMessage>(message =>
                    ((IMessageProviderConfigBuilder<TestMessage>)message).SetMessageProviderConfig(
                        new ClassBasedProviderConfig(static _ => "shard-1")
                    )
                );
            });

        // then — no conflict raised
        action.Should().NotThrow();
    }

    private sealed record TestMessage;

    private sealed record FakeProviderConfig(string Value);

    // Class-based (not a record) — simulates IProviderHeaderContributions configs like KafkaMessageConfig<T>.
    private sealed class ClassBasedProviderConfig(Func<TestMessage, string?> selector) : IProviderHeaderContributions
    {
        public IReadOnlyList<ProviderHeaderContribution> HeaderContributions { get; } =
        [new ProviderHeaderContribution("x-shard", message => selector((TestMessage)message))];
    }

    private sealed record OtherProviderConfig(string Value);

    private sealed class TestConsumer : IConsume<TestMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<TestMessage> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
