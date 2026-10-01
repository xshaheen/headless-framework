// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Registration;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Registration;

/// <summary>
/// Covers how a provider's consumer-scope settings, written through <c>Tune</c>, reach the consumer's registered metadata,
/// which is what a transport reads when it opens the consumer's subscription.
/// </summary>
public sealed class ConsumerTuningProviderConfigTests : TestBase
{
    [Fact]
    public void should_carry_tuned_provider_config_into_consumer_metadata()
    {
        // given
        var config = new FakeProviderConfig("consumer");
        var services = new ServiceCollection();
        services.AddHeadlessMessaging(setup =>
            setup
                .AddConsumer<TunedConsumer>()
                .Tune(
                    TunedConsumer.Identity,
                    consumer => ((IConsumerProviderConfigBuilder)consumer).SetConsumerProviderConfig(config)
                )
        );

        // when
        using var provider = services.BuildServiceProvider();
        var metadata = provider.GetRequiredService<ConsumerRegistry>().GetAll().Single();

        // then
        metadata.ProviderConfigs[typeof(FakeProviderConfig)].Should().Be(config);
    }

    [Fact]
    public void should_keep_the_last_config_of_a_type_and_merge_configs_of_other_types()
    {
        // given
        var replaced = new FakeProviderConfig("first");
        var kept = new FakeProviderConfig("second");
        var other = new OtherProviderConfig("other");
        var services = new ServiceCollection();
        services.AddHeadlessMessaging(setup =>
            setup
                .AddConsumer<TunedConsumer>()
                .Tune(
                    TunedConsumer.Identity,
                    consumer =>
                    {
                        var builder = (IConsumerProviderConfigBuilder)consumer;
                        builder.SetConsumerProviderConfig(replaced);
                        builder.SetConsumerProviderConfig(kept);
                        builder.SetConsumerProviderConfig(other);
                    }
                )
        );

        // when
        using var provider = services.BuildServiceProvider();
        var metadata = provider.GetRequiredService<ConsumerRegistry>().GetAll().Single();

        // then
        metadata.ProviderConfigs.Values.Should().BeEquivalentTo<object>([kept, other]);
    }

    private sealed record TunedMessage;

    private sealed record FakeProviderConfig(string Value);

    private sealed record OtherProviderConfig(string Value);

    [BusConsumer(Identity)]
    private sealed class TunedConsumer : IConsume<TunedMessage>
    {
        public const string Identity = "tests.provider-config.tuned";

        public ValueTask ConsumeAsync(ConsumeContext<TunedMessage> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
