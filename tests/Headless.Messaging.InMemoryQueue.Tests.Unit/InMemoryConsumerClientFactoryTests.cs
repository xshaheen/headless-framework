// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.InMemoryQueue;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class InMemoryConsumerClientFactoryTests : TestBase
{
    private readonly MemoryQueue _queue;
    private readonly InMemoryConsumerClientFactory _factory;

    public InMemoryConsumerClientFactoryTests()
    {
        var logger = Substitute.For<ILogger<MemoryQueue>>();
        _queue = new MemoryQueue(logger);
        _factory = new InMemoryConsumerClientFactory(_queue);
    }

    [Fact]
    public async Task should_create_consumer_client()
    {
        // when
        var client = await _factory.CreateAsync("test-group", 1);

        // then
        client.Should().NotBeNull();
        client.Should().BeOfType<InMemoryConsumerClient>();
        await client.DisposeAsync();
    }

    [Fact]
    public async Task should_create_client_with_specified_group_name()
    {
        // given
        const string groupName = "my-custom-group";

        // when
        var client = await _factory.CreateAsync(groupName, 1);

        // then
        client.Should().NotBeNull();
        client.BrokerAddress.Name.Should().Be("InMemory");
        await client.DisposeAsync();
    }

    [Fact]
    public async Task should_create_client_with_specified_concurrency()
    {
        // given
        const byte concurrency = 4;

        // when
        var client = await _factory.CreateAsync("test-group", concurrency);

        // then
        client.Should().NotBeNull();
        await client.DisposeAsync();
    }

    [Fact]
    public async Task should_create_client_with_zero_concurrency()
    {
        // given
        const byte concurrency = 0;

        // when
        var client = await _factory.CreateAsync("test-group", concurrency);

        // then - zero concurrency means sequential processing
        client.Should().NotBeNull();
        await client.DisposeAsync();
    }

    [Fact]
    public async Task should_return_iconsumer_client_interface()
    {
        // when
        var client = await _factory.CreateAsync("test-group", 1);

        // then
        client.Should().BeAssignableTo<IConsumerClient>();
        await client.DisposeAsync();
    }

    [Fact]
    public async Task should_create_multiple_clients_for_different_groups()
    {
        // when
        var client1 = await _factory.CreateAsync("group-1", 1);
        var client2 = await _factory.CreateAsync("group-2", 1);
        var client3 = await _factory.CreateAsync("group-3", 1);

        // then
        client1.Should().NotBeNull();
        client2.Should().NotBeNull();
        client3.Should().NotBeNull();

        await client1.DisposeAsync();
        await client2.DisposeAsync();
        await client3.DisposeAsync();
    }

    [Fact]
    public async Task should_create_client_that_shares_same_queue()
    {
        // given
        var client1 = await _factory.CreateAsync("group-1", 1);
        var client2 = await _factory.CreateAsync("group-2", 1);

        await client1.SubscribeAsync(["shared-topic"]);
        await client2.SubscribeAsync(["shared-topic"]);

        var messages1 = new List<object>();
        var messages2 = new List<object>();

        client1.OnMessageCallback = (msg, _) =>
        {
            messages1.Add(msg);
            return Task.CompletedTask;
        };
        client2.OnMessageCallback = (msg, _) =>
        {
            messages2.Add(msg);
            return Task.CompletedTask;
        };

        using var cts = new CancellationTokenSource();
        var listen1 = Task.Run(
            async () =>
            {
                try
                {
                    await client1.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );
        var listen2 = Task.Run(
            async () =>
            {
                try
                {
                    await client2.ListeningAsync(TimeSpan.FromSeconds(5), cts.Token);
                }
                catch (OperationCanceledException) { }
            },
            AbortToken
        );

        await Task.Delay(50, AbortToken);

        // when - send a message through the shared queue
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headless.Messaging.Messages.Headers.MessageId] = "test-id",
            [Headless.Messaging.Messages.Headers.MessageName] = "shared-topic",
        };
        var message = new Headless.Messaging.Messages.TransportMessage(headers, ReadOnlyMemory<byte>.Empty);
        _queue.Send(message);

        await Task.Delay(100, AbortToken);
        await cts.CancelAsync();

        // then - both clients should receive the message
        messages1.Should().HaveCount(1);
        messages2.Should().HaveCount(1);

        await client1.DisposeAsync();
        await client2.DisposeAsync();
    }
}
