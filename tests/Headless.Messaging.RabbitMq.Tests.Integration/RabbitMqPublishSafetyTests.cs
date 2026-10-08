// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

/// <summary>
/// Publish-side provisioning, unroutable returns, dead-lettering, and the shutdown drain against a real broker.
/// </summary>
[Collection<RabbitMqFixture>]
public sealed class RabbitMqPublishSafetyTests(RabbitMqFixture fixture) : TestBase
{
    private static readonly TimeSpan _Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task should_keep_queue_message_published_before_any_consumer_existed()
    {
        // given
        var options = _CreateOptions();
        var name = $"orders-{Guid.NewGuid():N}";
        await using var pool = _CreatePool(options);
        await using var transport = new RabbitMqTransport(
            NullLogger<RabbitMqTransport>.Instance,
            pool,
            MessageLane.Queue
        );

        // when
        var result = await transport.SendAsync(_CreateMessage(name), AbortToken);

        // then — the queue exists and holds the message for the consumer that starts later
        result.Succeeded.Should().BeTrue(result.Exception?.ToString());
        (await _MessageCountAsync($"queue.{name}")).Should().Be(1);
    }

    [Fact]
    public async Task should_fail_queue_publish_when_auto_provision_is_off_and_queue_is_missing()
    {
        // given
        var options = _CreateOptions();
        options.AutoProvision = false;
        await _DeclareExchangesAsOperatorAsync(options.ExchangeName);
        await using var pool = _CreatePool(options);
        await using var transport = new RabbitMqTransport(
            NullLogger<RabbitMqTransport>.Instance,
            pool,
            MessageLane.Queue
        );
        var name = $"orders-{Guid.NewGuid():N}";

        // when
        var result = await transport.SendAsync(_CreateMessage(name), AbortToken);

        // then — the passive declare proved nothing would receive it, and nothing was created
        result.Succeeded.Should().BeFalse();
        result.Exception!.InnerException.Should().BeOfType<OperationInterruptedException>();
        (await fixture.QueueExistsAsync($"queue.{name}", AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_queue_publish_the_broker_returns_as_unroutable()
    {
        // given — an operator-managed queue that nobody bound to the exchange
        var options = _CreateOptions();
        options.AutoProvision = false;
        await _DeclareExchangesAsOperatorAsync(options.ExchangeName);
        var name = $"orders-{Guid.NewGuid():N}";
        await _DeclareQueueAsOperatorAsync($"queue.{name}");
        await using var pool = _CreatePool(options);
        await using var transport = new RabbitMqTransport(
            NullLogger<RabbitMqTransport>.Instance,
            pool,
            MessageLane.Queue
        );

        // when
        var result = await transport.SendAsync(_CreateMessage(name), AbortToken);

        // then — mandatory publish plus publisher confirms turn the broker's basic.return into a failure
        result.Succeeded.Should().BeFalse();
        result.Exception!.InnerException.Should().BeOfType<PublishReturnException>();
    }

    [Fact]
    public async Task should_keep_malformed_envelope_in_dead_letter_queue_when_dead_lettering_is_enabled()
    {
        // given
        var options = _CreateOptions();
        options.QueueArguments.EnableDeadLettering = true;
        // An empty message id makes every delivery a malformed envelope, which the consumer rejects without requeue.
        options.CustomHeadersBuilder = static (_, _) => [new(MessagingHeaders.MessageId, string.Empty)];
        var name = $"orders-{Guid.NewGuid():N}";
        await using var pool = _CreatePool(options);
        await using var transport = new RabbitMqTransport(
            NullLogger<RabbitMqTransport>.Instance,
            pool,
            MessageLane.Queue
        );
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var consumer = new RabbitMqConsumerClient(
            "group",
            1,
            pool,
            Options.Create(options),
            services,
            lane: MessageLane.Queue
        );
        consumer.AttachCallbacks((_, _) => Task.CompletedTask, _ => { });
        await consumer.SubscribeAsync([name], AbortToken);
        using var listening = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listeningTask = consumer.ListeningAsync(TimeSpan.FromSeconds(1), listening.Token).AsTask();

        try
        {
            await consumer.WaitUntilReadyAsync(AbortToken);

            // when
            var result = await transport.SendAsync(_CreateMessage(name), AbortToken);

            // then
            result.Succeeded.Should().BeTrue(result.Exception?.ToString());
            await _WaitForMessageCountAsync($"queue.{name}.dlq", expected: 1);
            (await _MessageCountAsync($"queue.{name}")).Should().Be(0);
        }
        finally
        {
            await listening.CancelAsync();
            await listeningTask;
        }
    }

    [Fact]
    public async Task should_ack_in_flight_message_before_closing_channel_on_shutdown()
    {
        // given
        var options = _CreateOptions();
        var name = $"orders-{Guid.NewGuid():N}";
        await using var pool = _CreatePool(options);
        await using var transport = new RabbitMqTransport(
            NullLogger<RabbitMqTransport>.Instance,
            pool,
            MessageLane.Queue
        );
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var consumer = new RabbitMqConsumerClient(
            "group",
            2,
            pool,
            Options.Create(options),
            services,
            lane: MessageLane.Queue
        );
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.AttachCallbacks(
            async (_, sender) =>
            {
                handlerStarted.TrySetResult();
                await releaseHandler.Task;
                await consumer.CommitAsync(sender, CancellationToken.None);
            },
            _ => { }
        );
        await consumer.SubscribeAsync([name], AbortToken);
        using var listening = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var listeningTask = consumer.ListeningAsync(TimeSpan.FromSeconds(1), listening.Token).AsTask();
        await consumer.WaitUntilReadyAsync(AbortToken);
        (await transport.SendAsync(_CreateMessage(name), AbortToken)).Succeeded.Should().BeTrue();
        await handlerStarted.Task.WaitAsync(_Wait, AbortToken);
        await listening.CancelAsync();
        await listeningTask;

        // when — shutdown starts while the handler still runs, and the handler finishes inside the budget
        var shutdown = consumer.ShutdownAsync(_Wait, AbortToken).AsTask();
        releaseHandler.TrySetResult();
        await shutdown.WaitAsync(_Wait, AbortToken);

        // then — the ack reached the broker, so nothing returns to the queue for redelivery
        (await _MessageCountAsync($"queue.{name}"))
            .Should()
            .Be(0);
    }

    private RabbitMqMessagingOptions _CreateOptions()
    {
        return new RabbitMqMessagingOptions
        {
            HostName = fixture.HostName,
            Port = fixture.Port,
            UserName = fixture.UserName,
            Password = fixture.Password,
            ExchangeName = $"safety-{Guid.NewGuid():N}",
        };
    }

    private static ConnectionChannelPool _CreatePool(RabbitMqMessagingOptions options)
    {
        return new ConnectionChannelPool(
            NullLogger<ConnectionChannelPool>.Instance,
            Options.Create(new MessagingOptions { Version = "v1" }),
            Options.Create(options)
        );
    }

    private static TransportMessage _CreateMessage(string name)
    {
        return new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MessagingHeaders.MessageId] = Guid.NewGuid().ToString("N"),
                [MessagingHeaders.MessageName] = name,
            },
            "payload"u8.ToArray()
        );
    }

    private async Task _DeclareExchangesAsOperatorAsync(string baseExchange)
    {
        var connection = await fixture.GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: AbortToken);
        await channel.ExchangeDeclareAsync(
            $"{baseExchange}.bus",
            ExchangeType.Topic,
            durable: true,
            cancellationToken: AbortToken
        );
        await channel.ExchangeDeclareAsync(
            $"{baseExchange}.queue",
            ExchangeType.Direct,
            durable: true,
            cancellationToken: AbortToken
        );
    }

    private async Task _DeclareQueueAsOperatorAsync(string queueName)
    {
        var connection = await fixture.GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: AbortToken);
        await channel.QueueDeclareAsync(
            queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: AbortToken
        );
    }

    private async Task<uint> _MessageCountAsync(string queueName)
    {
        var connection = await fixture.GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: AbortToken);
        var declared = await channel.QueueDeclarePassiveAsync(queueName, AbortToken);
        return declared.MessageCount;
    }

    // Dead-lettering is asynchronous to the reject, so the count is read until it arrives or the wait ends.
    private async Task _WaitForMessageCountAsync(string queueName, uint expected)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(_Wait);
        uint count;
        while ((count = await _MessageCountAsync(queueName)) != expected && !timeout.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }
            catch (OperationCanceledException) when (!AbortToken.IsCancellationRequested)
            {
                // The wait ended; the assertion below reports the last count.
            }
        }

        count.Should().Be(expected);
    }
}
