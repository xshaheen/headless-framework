// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Nats;

namespace Tests;

public sealed class NatsMessageBuilderExtensionsTests
{
    [Fact]
    public void should_store_subject_shard_header_contribution()
    {
        var builder = new MessageContractBuilder<TestMessage>("tests.nats.message", "v1");

        builder.OnBus(bus => bus.UseNats(nats => nats.SubjectShard(static message => message.TenantId)));
        var contribution = (
            (IProviderHeaderContributions)builder.Build().Bus.ProviderConfigs.Values.Single()
        ).HeaderContributions.Single();

        contribution.HeaderName.Should().Be(NatsMessagingHeaders.SubjectShard);
        contribution.Selector(new TestMessage("tenant-a")).Should().Be("tenant-a");
    }

    [Theory]
    [InlineData("tenant.a")]
    [InlineData("tenant*")]
    [InlineData("tenant>")]
    [InlineData("tenant a")]
    [InlineData("tenant\r")]
    [InlineData("tenant\n")]
    [InlineData("tenant\t")]
    public void should_reject_invalid_subject_shard_tokens(string shard)
    {
        var builder = new MessageContractBuilder<TestMessage>("tests.nats.message", "v1");
        builder.OnBus(bus => bus.UseNats(nats => nats.SubjectShard(_ => shard)));
        var contribution = (
            (IProviderHeaderContributions)builder.Build().Bus.ProviderConfigs.Values.Single()
        ).HeaderContributions.Single();

        var act = () => contribution.Selector(new TestMessage("tenant-a"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*SubjectShard*");
    }

    [Fact]
    public void validate_returns_null_for_null_shard()
    {
        NatsSubjectShard.Validate(null).Should().BeNull();
    }

    [Fact]
    public void validate_returns_valid_token_unchanged()
    {
        NatsSubjectShard.Validate("tenant-a").Should().Be("tenant-a");
    }

    [Fact]
    public void validate_throws_for_empty_shard()
    {
        var act = () => NatsSubjectShard.Validate("");
        act.Should().Throw<InvalidOperationException>().WithMessage("*SubjectShard*");
    }

    [Fact]
    public void validate_throws_for_oversized_shard()
    {
        var act = () => NatsSubjectShard.Validate(new string('a', 257));
        act.Should().Throw<InvalidOperationException>().WithMessage("*SubjectShard*");
    }

    [Fact]
    public void should_store_consumer_config_when_tuning_a_declared_consumer()
    {
        var tuning = new ConsumerTuningBuilder("tests.nats.tuned");

        tuning.UseNats(nats => nats.Sharded());

        tuning.Build().ProviderConfigs.Values.Single().Should().BeEquivalentTo(new NatsConsumerConfig(IsSharded: true));
    }

    [Fact]
    public void should_store_consumer_limits_when_tuning_a_declared_consumer()
    {
        var tuning = new ConsumerTuningBuilder("tests.nats.tuned");

        tuning.UseNats(nats =>
            nats.AckWait(TimeSpan.FromMinutes(2))
                .MaxAckPending(64)
                .MaxDeliver(5)
                .InactiveThreshold(TimeSpan.FromDays(1))
        );

        tuning
            .Build()
            .ProviderConfigs.Values.Single()
            .Should()
            .BeEquivalentTo(
                new NatsConsumerConfig(
                    IsSharded: false,
                    AckWait: TimeSpan.FromMinutes(2),
                    MaxAckPending: 64,
                    MaxDeliver: 5,
                    InactiveThreshold: TimeSpan.FromDays(1)
                )
            );
    }

    [Fact]
    public void should_reject_non_positive_consumer_limits()
    {
        var builder = new NatsConsumerConfigBuilder();

        ((Action)(() => builder.AckWait(TimeSpan.Zero))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => builder.MaxAckPending(0))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => builder.MaxDeliver(-1))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => builder.InactiveThreshold(TimeSpan.FromSeconds(-1))))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_leave_unset_consumer_limits_as_configured_when_applied()
    {
        var config = new NATS.Client.JetStream.Models.ConsumerConfig("durable")
        {
            AckWait = TimeSpan.FromSeconds(30),
            MaxAckPending = 1000,
        };

        new NatsConsumerConfig(IsSharded: false, MaxDeliver: 3).ApplyTo(config);

        config.AckWait.Should().Be(TimeSpan.FromSeconds(30));
        config.MaxAckPending.Should().Be(1000);
        config.MaxDeliver.Should().Be(3);
        config.InactiveThreshold.Should().Be(TimeSpan.Zero);
    }

    private sealed record TestMessage(string TenantId);
}
