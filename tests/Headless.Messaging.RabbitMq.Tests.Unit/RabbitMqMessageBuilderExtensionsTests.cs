// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.RabbitMq;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class RabbitMqMessageBuilderExtensionsTests
{
    [Fact]
    public void should_store_routing_key_header_contribution()
    {
        var builder = new MessageBuilder<TestMessage>(new ServiceCollection());

        builder.UseRabbitMq(rabbit => rabbit.RoutingKeyFromMessage(static message => $"tenant.{message.TenantId}"));
        var contribution = ((IProviderHeaderContributions)builder.Build().ProviderConfigs.Values.Single())
            .HeaderContributions.Single();

        contribution.HeaderName.Should().Be(RabbitMqHeaders.RoutingKey);
        contribution.Selector(new TestMessage("a")).Should().Be("tenant.a");
    }

    [Fact]
    public void should_store_consumer_prefetch_config()
    {
        var builder = new MessageBuilder<TestMessage>(new ServiceCollection());

        builder.OnBus<TestConsumer>(consumer => consumer.UseRabbitMq(rabbit => rabbit.PrefetchCount(20)));
        var config = builder.Build().Consumers.Single().ProviderConfigs.Values.Single();

        config.Should().BeEquivalentTo(new RabbitMqConsumerConfig(20));
    }

    private sealed record TestMessage(string TenantId);

    private sealed class TestConsumer : IConsume<TestMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<TestMessage> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
