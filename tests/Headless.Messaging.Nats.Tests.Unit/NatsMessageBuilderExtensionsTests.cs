// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Messaging.Registration;

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

    private sealed record TestMessage(string TenantId);
}
