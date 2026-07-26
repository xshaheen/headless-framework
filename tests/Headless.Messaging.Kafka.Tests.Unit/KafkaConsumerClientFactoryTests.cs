// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Kafka;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class KafkaConsumerClientFactoryTests : TestBase
{
    private readonly IOptions<KafkaMessagingOptions> _options = Options.Create(
        new KafkaMessagingOptions { Servers = "localhost:9092" }
    );

    [Fact]
    public async Task should_preserve_factory_cancellation()
    {
        var factory = new KafkaConsumerClientFactory(_options, new ServiceCollection().BuildServiceProvider());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await factory.CreateAsync("test-group", 1, MessageLane.Queue, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_create_consumer_client_with_correct_group_name()
    {
        // given
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var factory = new KafkaConsumerClientFactory(_options, serviceProvider);

        // when
        var client = await factory.CreateAsync("test-consumer-group", 1, MessageLane.Queue, AbortToken);

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
        var client = await factory.CreateAsync("test-consumer-group", 5, MessageLane.Queue, AbortToken);

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
        var client1 = await factory.CreateAsync("group-1", 1, MessageLane.Queue, AbortToken);
        var client2 = await factory.CreateAsync("group-2", 1, MessageLane.Queue, AbortToken);

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
        var client = await factory.CreateAsync("test-group", 1, MessageLane.Queue, AbortToken);

        // then
        client.BrokerAddress.Name.Should().Be("kafka");
        client.BrokerAddress.Endpoint.Should().Be("localhost:9092");
        await client.DisposeAsync();
    }

    [Fact]
    public async Task should_reject_bus_consumer_lane()
    {
        // given
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var factory = new KafkaConsumerClientFactory(_options, serviceProvider);

        // when
        var act = async () => await factory.CreateAsync("test-group", 1, MessageLane.Bus);

        // then
        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
