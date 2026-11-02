// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Pulsar;
using Headless.Testing.Tests;

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
    public void should_apply_no_ack_timeout_when_neither_ack_timeout_nor_dead_letter_is_set()
    {
        new PulsarConsumerConfig(KeyShared: false).EffectiveAckTimeout.Should().BeNull();
    }

    [Fact]
    public void should_apply_the_dead_letter_ack_timeout_when_a_dead_letter_policy_sets_none()
    {
        new PulsarConsumerConfig(KeyShared: false, MaxRedeliveryCount: 2)
            .EffectiveAckTimeout.Should()
            .Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void should_apply_the_configured_ack_timeout_over_the_dead_letter_default()
    {
        new PulsarConsumerConfig(KeyShared: false, MaxRedeliveryCount: 2, AckTimeout: TimeSpan.FromSeconds(45))
            .EffectiveAckTimeout.Should()
            .Be(TimeSpan.FromSeconds(45));
    }
}
