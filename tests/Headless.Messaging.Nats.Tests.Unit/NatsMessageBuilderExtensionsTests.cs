// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Nats;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class NatsMessageBuilderExtensionsTests
{
    [Fact]
    public void should_store_subject_shard_header_contribution()
    {
        var builder = new MessageBuilder<TestMessage>(new ServiceCollection());

        builder.UseNats(nats => nats.SubjectShard(static message => message.TenantId));
        var contribution = (
            (IProviderHeaderContributions)builder.Build().ProviderConfigs.Values.Single()
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
        var builder = new MessageBuilder<TestMessage>(new ServiceCollection());
        builder.UseNats(nats => nats.SubjectShard(_ => shard));
        var contribution = (
            (IProviderHeaderContributions)builder.Build().ProviderConfigs.Values.Single()
        ).HeaderContributions.Single();

        var act = () => contribution.Selector(new TestMessage("tenant-a"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*SubjectShard*");
    }

    [Fact]
    public void Validate_returns_null_for_null_shard()
    {
        NatsSubjectShard.Validate(null).Should().BeNull();
    }

    [Fact]
    public void Validate_returns_valid_token_unchanged()
    {
        NatsSubjectShard.Validate("tenant-a").Should().Be("tenant-a");
    }

    [Fact]
    public void Validate_throws_for_empty_shard()
    {
        var act = () => NatsSubjectShard.Validate("");
        act.Should().Throw<InvalidOperationException>().WithMessage("*SubjectShard*");
    }

    [Fact]
    public void Validate_throws_for_oversized_shard()
    {
        var act = () => NatsSubjectShard.Validate(new string('a', 257));
        act.Should().Throw<InvalidOperationException>().WithMessage("*SubjectShard*");
    }

    private sealed record TestMessage(string TenantId);
}
