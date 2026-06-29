// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.InMemory;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class InMemoryBusTransportTests : TestBase
{
    [Fact]
    public async Task should_return_in_memory_broker_address()
    {
        // given
        var queueLogger = Substitute.For<ILogger<MemoryQueue>>();
        var transportLogger = Substitute.For<ILogger<InMemoryBusTransport>>();
        await using var transport = new InMemoryBusTransport(new MemoryQueue(queueLogger), transportLogger);

        // when
        var address = transport.BrokerAddress;

        // then
        address.Name.Should().Be("InMemory");
        address.Endpoint.Should().Be("localhost");
    }

    [Fact]
    public async Task should_fan_out_message_to_every_subscribed_group()
    {
        // given
        var queueLogger = Substitute.For<ILogger<MemoryQueue>>();
        var transportLogger = Substitute.For<ILogger<InMemoryBusTransport>>();
        var queue = new MemoryQueue(queueLogger);
        await using var transport = new InMemoryBusTransport(queue, transportLogger);
        await using var client1 = new InMemoryConsumerClient(queue, "group-1", 1);
        await using var client2 = new InMemoryConsumerClient(queue, "group-2", 1);
        await using var client3 = new InMemoryConsumerClient(queue, "group-3", 1);

        await client1.SubscribeAsync(["events"]);
        await client2.SubscribeAsync(["events"]);
        await client3.SubscribeAsync(["events"]);

        var received = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task onMessage(TransportMessage message, object? __)
        {
            var group = message.Headers[Headers.Group]!;
            received.AddOrUpdate(group, 1, (_, count) => count + 1);

            if (received.Values.Sum() == 3)
            {
                tcs.TrySetResult();
            }

            return Task.CompletedTask;
        }

        client1.OnMessageCallback = onMessage;
        client2.OnMessageCallback = onMessage;
        client3.OnMessageCallback = onMessage;

        using var cts = new CancellationTokenSource();
        var listen1 = Task.Run(() => _ListenAsync(client1, cts.Token), AbortToken);
        var listen2 = Task.Run(() => _ListenAsync(client2, cts.Token), AbortToken);
        var listen3 = Task.Run(() => _ListenAsync(client3, cts.Token), AbortToken);

        await Task.Delay(50, AbortToken);

        // when
        var result = await transport.SendAsync(_CreateTestMessage("msg-1", "events"), AbortToken);
        _ = await Task.WhenAny(tcs.Task, Task.Delay(5000, AbortToken));
        await cts.CancelAsync();

        // then
        result.Succeeded.Should().BeTrue();
        received
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["group-1"] = 1,
                    ["group-2"] = 1,
                    ["group-3"] = 1,
                }
            );
    }

    [Fact]
    public async Task should_return_success_when_no_bus_subscriber()
    {
        // given — no subscriber registered; real-broker semantics: publish-without-subscriber is a no-op
        var queueLogger = Substitute.For<ILogger<MemoryQueue>>();
        var transportLogger = Substitute.For<ILogger<InMemoryBusTransport>>();
        await using var transport = new InMemoryBusTransport(new MemoryQueue(queueLogger), transportLogger);

        // when
        var result = await transport.SendAsync(_CreateTestMessage("msg-1", "unsubscribed-messageName"), AbortToken);

        // then — message is silently dropped; transport reports success (the send itself did not fail)
        result.Succeeded.Should().BeTrue();
    }

    private static async Task _ListenAsync(InMemoryConsumerClient client, CancellationToken cancellationToken)
    {
        try
        {
            await client.ListeningAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (OperationCanceledException) { }
    }

    private static TransportMessage _CreateTestMessage(string id, string messageName)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = id,
            [Headers.MessageName] = messageName,
        };

        return new TransportMessage(headers, ReadOnlyMemory<byte>.Empty);
    }
}
