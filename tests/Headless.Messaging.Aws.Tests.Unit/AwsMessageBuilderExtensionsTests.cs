// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Messaging.Registration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class AwsMessageBuilderExtensionsTests
{
    [Fact]
    public void should_store_message_group_id_header_contribution()
    {
        var builder = new MessageBuilder<TestMessage>(new ServiceCollection());

        builder.UseAws(aws => aws.MessageGroupId(static message => message.TenantId));
        var contribution = (
            (IProviderHeaderContributions)builder.Build().ProviderConfigs.Values.Single()
        ).HeaderContributions.Single();

        contribution.HeaderName.Should().Be(AwsMessagingHeaders.MessageGroupId);
        contribution.Selector(new TestMessage("tenant-a")).Should().Be("tenant-a");
    }

    [Fact]
    public void should_reject_message_group_id_longer_than_sqs_limit()
    {
        var builder = new MessageBuilder<TestMessage>(new ServiceCollection());
        builder.UseAws(aws => aws.MessageGroupId(static _ => new string('x', 129)));
        var contribution = (
            (IProviderHeaderContributions)builder.Build().ProviderConfigs.Values.Single()
        ).HeaderContributions.Single();

        var act = () => contribution.Selector(new TestMessage("tenant-a"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*MessageGroupId*128*");
    }

    private sealed record TestMessage(string TenantId);
}
