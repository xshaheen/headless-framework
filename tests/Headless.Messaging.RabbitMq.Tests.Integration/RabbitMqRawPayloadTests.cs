// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Messaging.RabbitMq;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// A message a non-.NET producer publishes with a plain AMQP client, to the address Headless derives and with none of
/// the Headless envelope headers, reaching a consumer whose contract accepts raw payloads.
/// </summary>
[Collection<RabbitMqFixture>]
public sealed class RabbitMqRawPayloadTests(RabbitMqFixture fixture) : TestBase
{
    private static readonly TimeSpan _Wait = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task should_run_the_handler_once_when_a_raw_message_id_is_delivered_twice()
    {
        // given
        await using var host = await _StartHostAsync();
        var producedAt = DateTimeOffset.FromUnixTimeSeconds(1_791_000_000);
        var duplicateId = $"producer-{Guid.NewGuid():N}";
        var nextId = $"producer-{Guid.NewGuid():N}";

        // when: the producer sends one message twice under its own idempotency key, then another one
        await _PublishNativeAsync(host, """{"value":"first"}""", duplicateId, producedAt);
        await _PublishNativeAsync(host, """{"value":"first"}""", duplicateId, producedAt);
        await _PublishNativeAsync(host, """{"value":"second"}""", nextId, producedAt);

        // then
        await host.Consumer.WaitForCountAsync(2, _Wait, AbortToken);
        await _WaitForQueueDepthAsync(host.QueueName, 0);
        var received = host.Consumer.Received.ToArray();
        received.Select(r => r.MessageId).Should().BeEquivalentTo([duplicateId, nextId]);
        received.Select(r => r.Value).Should().BeEquivalentTo(["first", "second"]);

        var first = received.Single(r => string.Equals(r.MessageId, duplicateId, StringComparison.Ordinal));
        first.MessageName.Should().Be(host.MessageName);
        first.CorrelationId.Should().Be(duplicateId);
        first.ContractVersion.Should().Be("2");
        first.Timestamp.Should().Be(producedAt);
    }

    [Fact]
    public async Task should_terminally_reject_a_raw_delivery_without_a_message_id_and_never_run_the_handler()
    {
        // given
        await using var host = await _StartHostAsync();

        // when
        await _PublishNativeAsync(host, """{"value":"anonymous"}""", messageId: null, producedAt: null);

        // then
        await _WaitForQueueDepthAsync(host.QueueName + ".dlq", 1);
        await _WaitForQueueDepthAsync(host.QueueName, 0);
        host.Consumer.Received.Should().BeEmpty();
    }

    [Fact]
    public async Task should_poison_a_raw_delivery_with_a_malformed_body_without_redelivering_it()
    {
        // given
        await using var host = await _StartHostAsync();
        var messageId = $"producer-{Guid.NewGuid():N}";

        // when
        await _PublishNativeAsync(host, "{not json", messageId, producedAt: null);

        // then
        await _WaitForQueueDepthAsync(host.QueueName + ".dlq", 1);
        await _WaitForQueueDepthAsync(host.QueueName, 0);
        var poisoned = await _WaitForReceivedRowAsync(host, messageId);
        poisoned.StatusName.Should().Be(StatusName.Failed);
        host.Consumer.Received.Should().BeEmpty();
    }

    private async Task<RawHost> _StartHostAsync()
    {
        var name = $"raw-{Guid.NewGuid():N}";
        var exchange = $"raw-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<RawRunConsumer>();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseRabbitMq(options =>
            {
                options.HostName = fixture.HostName;
                options.Port = fixture.Port;
                options.UserName = fixture.UserName;
                options.Password = fixture.Password;
                options.ExchangeName = exchange;
                options.QueueArguments.EnableDeadLettering = true;
            });
            setup.UseInMemoryStorage();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<RawRun>(name, "2").OnQueue(queue => queue.RawPayload());
            messaging.AddModule<RawRunModule>();
        });

        var provider = services.BuildServiceProvider();

        try
        {
            await provider.SetupResourcesAndBootstrapAsync(AbortToken);
            var host = new RawHost(
                provider,
                name,
                RabbitMqPhysicalAddress.Exchange(exchange, MessageLane.Queue),
                RabbitMqPhysicalAddress.RoutingKey(MessageLane.Queue, name),
                RabbitMqPhysicalAddress.Queue(MessageLane.Queue, name, name)
            );
            await _WaitForConsumerAsync(host.QueueName);
            return host;
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }

    private async Task _PublishNativeAsync(RawHost host, string json, string? messageId, DateTimeOffset? producedAt)
    {
        var connection = await fixture.GetConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: AbortToken);
        var properties = new BasicProperties { ContentType = "application/json", Persistent = true };

        if (messageId is not null)
        {
            properties.MessageId = messageId;
        }

        if (producedAt is { } timestamp)
        {
            properties.Timestamp = new AmqpTimestamp(timestamp.ToUnixTimeSeconds());
        }

        await channel.BasicPublishAsync(
            host.Exchange,
            host.RoutingKey,
            mandatory: false,
            properties,
            Encoding.UTF8.GetBytes(json),
            AbortToken
        );
    }

    private async Task _WaitForConsumerAsync(string queueName)
    {
        await _WaitUntilAsync(
            async () =>
            {
                if (!await fixture.QueueExistsAsync(queueName, AbortToken))
                {
                    return false;
                }

                var connection = await fixture.GetConnectionAsync();
                await using var channel = await connection.CreateChannelAsync(cancellationToken: AbortToken);
                var declared = await channel.QueueDeclarePassiveAsync(queueName, AbortToken);
                return declared.ConsumerCount > 0;
            },
            $"queue '{queueName}' has a consumer"
        );
    }

    // Counts ready and unacknowledged messages, so a delivery still being handled keeps the queue from reading empty.
    private Task _WaitForQueueDepthAsync(string queueName, int expected)
    {
        return _WaitUntilAsync(
            async () =>
            {
                var result = await fixture.Container.ExecAsync(
                    ["rabbitmqctl", "list_queues", "-q", "--no-table-headers", "name", "messages"],
                    AbortToken
                );

                var depth = result
                    .Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(static line => line.Split('\t'))
                    .Where(columns => string.Equals(columns[0], queueName, StringComparison.Ordinal))
                    .Select(static columns => int.Parse(columns[1], CultureInfo.InvariantCulture))
                    .FirstOrDefault();

                return depth == expected;
            },
            $"queue '{queueName}' holds {expected} message(s)"
        );
    }

    private async Task<MessageView> _WaitForReceivedRowAsync(RawHost host, string messageId)
    {
        var monitoring = host.Services.GetRequiredService<IDataStorage>().GetMonitoringApi();
        MessageView? row = null;

        await _WaitUntilAsync(
            async () =>
            {
                var page = await monitoring.GetMessagesAsync(
                    new MessageQuery
                    {
                        MessageType = MessageType.Subscribe,
                        Name = host.MessageName,
                        Lane = MessageLane.Queue,
                        PageSize = 10,
                    },
                    AbortToken
                );

                row = page.Items.FirstOrDefault(item =>
                    string.Equals(item.MessageId, messageId, StringComparison.Ordinal)
                );
                return row is not null;
            },
            $"a received row for message '{messageId}'"
        );

        return row!;
    }

    private static async Task _WaitUntilAsync(Func<Task<bool>> condition, string description)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(_Wait);

        while (!await condition())
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }
            catch (OperationCanceledException e) when (!AbortToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out waiting until {description}.", e);
            }
        }
    }

    private sealed class RawHost(
        ServiceProvider services,
        string messageName,
        string exchange,
        string routingKey,
        string queueName
    ) : IAsyncDisposable
    {
        public IServiceProvider Services => services;

        public RawRunConsumer Consumer { get; } = services.GetRequiredService<RawRunConsumer>();

        public string MessageName => messageName;

        public string Exchange => exchange;

        public string RoutingKey => routingKey;

        public string QueueName => queueName;

        public ValueTask DisposeAsync() => services.DisposeAsync();
    }

    public sealed record RawRun([property: JsonPropertyName("value")] string Value);

    public sealed record RawRunReceipt(
        string MessageId,
        string MessageName,
        string? CorrelationId,
        string ContractVersion,
        DateTimeOffset Timestamp,
        string Value
    );

    public sealed class RawRunModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddQueueConsumer<RawRunConsumer, RawRun>(
                "tests.rabbitmq.raw-run",
                TestConsumerDispatch.FromServices<RawRunConsumer, RawRun>()
            );
    }

    public sealed class RawRunConsumer : IConsume<RawRun>
    {
        public ConcurrentQueue<RawRunReceipt> Received { get; } = new();

        public ValueTask ConsumeAsync(ConsumeContext<RawRun> context, CancellationToken cancellationToken)
        {
            Received.Enqueue(
                new RawRunReceipt(
                    context.MessageId,
                    context.MessageName,
                    context.CorrelationId,
                    context.ContractVersion,
                    context.Timestamp,
                    context.Message.Value
                )
            );
            return ValueTask.CompletedTask;
        }

        public async Task WaitForCountAsync(int count, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);

            while (Received.Count < count)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token);
            }
        }
    }
}
