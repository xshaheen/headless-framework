// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;
using Pulsar.Client.Api;
using Pulsar.Client.Common;

namespace Tests;

public sealed class PulsarMessageBuilderExtensionsTests : TestBase
{
    [Fact]
    public void should_store_consumer_config_when_tuning_a_declared_consumer()
    {
        // given
        var tuning = new ConsumerTuningBuilder("tests.pulsar.tuned");

        // when
        tuning.UsePulsar(pulsar =>
            pulsar
                .KeyShared()
                .DeadLetter(5, "persistent://public/default/orders-dlq")
                .AckTimeout(TimeSpan.FromMinutes(1))
        );

        // then
        tuning
            .Build()
            .ProviderConfigs.Values.Single()
            .Should()
            .BeEquivalentTo(
                new PulsarConsumerConfig(
                    KeyShared: true,
                    MaxRedeliveryCount: 5,
                    DeadLetterTopic: "persistent://public/default/orders-dlq",
                    AckTimeout: TimeSpan.FromMinutes(1)
                )
            );
    }

    [Fact]
    public void should_reject_out_of_range_consumer_options()
    {
        var builder = new PulsarConsumerConfigBuilder();

        ((Action)(() => builder.AckTimeout(TimeSpan.FromMilliseconds(999))))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
        ((Action)(() => builder.DeadLetter(0))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => builder.DeadLetter(-1))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => builder.DeadLetter(3, " "))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_options_when_tuning_a_consumer_with_an_out_of_range_value()
    {
        var tuning = new ConsumerTuningBuilder("tests.pulsar.tuned");

        var act = () => tuning.UsePulsar(pulsar => pulsar.AckTimeout(TimeSpan.FromMilliseconds(500)));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task should_match_the_shortest_ack_timeout_pulsar_client_accepts()
    {
        // Building the client opens no connection; only subscribing would.
        var client = await new PulsarClientBuilder().ServiceUrl("pulsar://localhost:6650").BuildAsync();

        try
        {
            var shortest = () => client.NewConsumer().AckTimeout(PulsarConsumerConfigBuilder.MinAckTimeout);
            var shorter = () =>
                client
                    .NewConsumer()
                    .AckTimeout(PulsarConsumerConfigBuilder.MinAckTimeout - TimeSpan.FromMilliseconds(1));

            shortest.Should().NotThrow();
            shorter.Should().Throw<ArgumentException>();
        }
        finally
        {
            await client.CloseAsync();
        }
    }

    [Fact]
    public async Task should_subscribe_shared_when_no_consumer_options_are_set()
    {
        await _WithConsumerBuilderAsync(builder =>
        {
            var configuration = PulsarConsumerClient
                .ConfigureSubscription(builder, everyInstance: false, config: null)
                .Configuration;

            configuration.SubscriptionType.Should().Be(SubscriptionType.Shared);
            configuration.KeySharedPolicy.Should().BeNull();
            configuration.DeadLetterPolicy.Should().BeNull();
            configuration.AckTimeout.Should().Be(TimeSpan.Zero);
        });
    }

    [Fact]
    public async Task should_apply_key_shared_dead_letter_and_ack_timeout_to_a_competing_subscription()
    {
        await _WithConsumerBuilderAsync(builder =>
        {
            var config = new PulsarConsumerConfig(
                KeyShared: true,
                MaxRedeliveryCount: 4,
                DeadLetterTopic: "orders-dlq",
                AckTimeout: TimeSpan.FromSeconds(45)
            );

            var configuration = PulsarConsumerClient
                .ConfigureSubscription(builder, everyInstance: false, config)
                .Configuration;

            configuration.SubscriptionType.Should().Be(SubscriptionType.KeyShared);
            configuration.KeySharedPolicy.Value.Should().BeOfType<KeySharedPolicyAutoSplit>();
            configuration.DeadLetterPolicy.Value.MaxRedeliveryCount.Should().Be(4);
            configuration.DeadLetterPolicy.Value.DeadLetterTopic.Should().Be("orders-dlq");
            configuration.AckTimeout.Should().Be(TimeSpan.FromSeconds(45));
        });
    }

    [Fact]
    public async Task should_leave_the_default_dead_letter_topic_to_pulsar_client_when_none_is_set()
    {
        await _WithConsumerBuilderAsync(builder =>
        {
            var configuration = PulsarConsumerClient
                .ConfigureSubscription(builder, everyInstance: false, new PulsarConsumerConfig(false, 2))
                .Configuration;

            configuration.SubscriptionType.Should().Be(SubscriptionType.Shared);
            configuration.DeadLetterPolicy.Value.DeadLetterTopic.Should().BeNull();
            configuration.AckTimeout.Should().Be(TimeSpan.Zero);
        });
    }

    [Fact]
    public async Task should_keep_an_every_instance_subscription_exclusive_and_non_durable()
    {
        await _WithConsumerBuilderAsync(builder =>
        {
            var configuration = PulsarConsumerClient
                .ConfigureSubscription(
                    builder,
                    everyInstance: true,
                    new PulsarConsumerConfig(false, AckTimeout: TimeSpan.FromSeconds(5))
                )
                .Configuration;

            configuration.SubscriptionType.Should().Be(SubscriptionType.Exclusive);
            configuration.SubscriptionMode.Should().Be(SubscriptionMode.NonDurable);
            configuration.AckTimeout.Should().Be(TimeSpan.FromSeconds(5));
        });
    }

    private static async Task _WithConsumerBuilderAsync(Action<ConsumerBuilder<byte[]>> assert)
    {
        // Building the client opens no connection; only subscribing would.
        var client = await new PulsarClientBuilder().ServiceUrl("pulsar://localhost:6650").BuildAsync();

        try
        {
            assert(client.NewConsumer());
        }
        finally
        {
            await client.CloseAsync();
        }
    }
}
