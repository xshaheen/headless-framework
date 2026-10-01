// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Headless.Messaging;
using Headless.Messaging.Kafka;
using Headless.Messaging.Registration;

namespace Tests;

public sealed class KafkaMessageBuilderExtensionsTests
{
    [Fact]
    public void should_store_partition_key_header_contribution()
    {
        var builder = new MessageContractBuilder<TestMessage>("tests.kafka.message", "v1");

        builder.OnQueue(queue => queue.UseKafka(kafka => kafka.PartitionBy(static message => message.TenantId)));
        var contribution = (
            (IProviderHeaderContributions)builder.Build().Queue.ProviderConfigs.Values.Single()
        ).HeaderContributions.Single();

        contribution.HeaderName.Should().Be(KafkaMessagingHeaders.KafkaKey);
        contribution.Selector(new TestMessage("tenant-a")).Should().Be("tenant-a");
    }

    [Fact]
    public void should_store_consumer_config_when_tuning_a_declared_consumer()
    {
        var tuning = new ConsumerTuningBuilder("tests.kafka.tuned");

        tuning.UseKafka(kafka => kafka.WithIsolationLevel(IsolationLevel.ReadCommitted));

        tuning
            .Build()
            .ProviderConfigs.Values.Single()
            .Should()
            .BeEquivalentTo(new KafkaConsumerConfig(IsolationLevel.ReadCommitted));
    }

    private sealed record TestMessage(string TenantId);
}
