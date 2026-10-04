// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests;

public sealed class RabbitMqMessageBuilderExtensionsTests
{
    [Fact]
    public void should_store_consumer_config_when_tuning_a_declared_consumer()
    {
        var tuning = new ConsumerTuningBuilder("tests.rabbitmq.tuned");

        tuning.UseRabbitMq(rabbit => rabbit.PrefetchCount(20));

        tuning.Build().ProviderConfigs.Values.Single().Should().BeEquivalentTo(new RabbitMqConsumerConfig(20));
    }
}
