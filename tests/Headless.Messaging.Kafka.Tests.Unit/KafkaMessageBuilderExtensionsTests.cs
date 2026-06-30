// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Headless.Messaging;
using Headless.Messaging.Kafka;
using Headless.Messaging.Registration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class KafkaMessageBuilderExtensionsTests
{
    [Fact]
    public void should_store_partition_key_header_contribution()
    {
        var builder = new MessageBuilder<TestMessage>(new ServiceCollection());

        builder.UseKafka(kafka => kafka.PartitionBy(static message => message.TenantId));
        var contribution = (
            (IProviderHeaderContributions)builder.Build().ProviderConfigs.Values.Single()
        ).HeaderContributions.Single();

        contribution.HeaderName.Should().Be(KafkaHeaders.KafkaKey);
        contribution.Selector(new TestMessage("tenant-a")).Should().Be("tenant-a");
    }

    [Fact]
    public void should_store_consumer_config_without_partition_surface()
    {
        var builder = new MessageBuilder<TestMessage>(new ServiceCollection());

        builder.OnQueue<TestConsumer>(consumer =>
            consumer.UseKafka(kafka => kafka.WithIsolationLevel(IsolationLevel.ReadCommitted))
        );
        var config = builder.Build().Consumers.Single().ProviderConfigs.Values.Single();

        config.Should().BeEquivalentTo(new KafkaConsumerConfig(IsolationLevel.ReadCommitted));
    }

    private sealed record TestMessage(string TenantId);

    private sealed class TestConsumer : IConsume<TestMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<TestMessage> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
