// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Kafka;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class KafkaConsumerClientFactoryTests : TestBase
{
    private readonly IOptions<MessagingKafkaOptions> _options = Options.Create(
        new MessagingKafkaOptions { Servers = "localhost:9092" }
    );

    [Fact]
    public async Task should_create_consumer_client_with_correct_group_name()
    {
        // given
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var factory = new KafkaConsumerClientFactory(_options, serviceProvider);

        // when
        var client = await factory.CreateAsync("test-consumer-group", 1);

        // then
        client.Should().NotBeNull();
        client.Should().BeOfType<KafkaConsumerClient>();
        await client.DisposeAsync();
    }

    [Fact]
    public async Task should_create_consumer_client_with_specified_concurrency()
    {
        // given
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var factory = new KafkaConsumerClientFactory(_options, serviceProvider);

        // when
        var client = await factory.CreateAsync("test-consumer-group", 5);

        // then
        client.Should().NotBeNull();
        await client.DisposeAsync();
    }

    [Fact]
    public async Task should_create_multiple_independent_clients()
    {
        // given
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var factory = new KafkaConsumerClientFactory(_options, serviceProvider);

        // when
        var client1 = await factory.CreateAsync("group-1", 1);
        var client2 = await factory.CreateAsync("group-2", 1);

        // then
        client1.Should().NotBeSameAs(client2);
        await client1.DisposeAsync();
        await client2.DisposeAsync();
    }

    [Fact]
    public async Task should_have_correct_broker_address()
    {
        // given
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var factory = new KafkaConsumerClientFactory(_options, serviceProvider);

        // when
        var client = await factory.CreateAsync("test-group", 1);

        // then
        client.BrokerAddress.Name.Should().Be("kafka");
        client.BrokerAddress.Endpoint.Should().Be("localhost:9092");
        await client.DisposeAsync();
    }
}
