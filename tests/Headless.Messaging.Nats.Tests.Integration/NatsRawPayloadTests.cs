// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// A message a non-.NET producer publishes with the plain NATS client, to the subject Headless derives and with none of
/// the Headless envelope headers, reaching a consumer whose contract accepts raw payloads.
/// </summary>
[Collection("Nats")]
public sealed class NatsRawPayloadTests(NatsFixture fixture) : TestBase
{
    private static readonly TimeSpan _Wait = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task should_consume_a_headerless_message_once_across_a_redelivery_of_the_stored_message()
    {
        // given
        await using var host = await _StartHostAsync();
        var producerId = $"producer-{Guid.NewGuid():N}";

        // when: one message without any header, and one carrying only JetStream's own idempotency key
        await _PublishNativeAsync(host, """{"value":"first"}""", natsMsgId: null);
        await _PublishNativeAsync(host, """{"value":"second"}""", producerId);

        // then
        await host.Consumer.WaitForCountAsync(2, _Wait, AbortToken);
        var received = host.Consumer.Received.ToArray();
        var first = received.Single(r => string.Equals(r.Value, "first", StringComparison.Ordinal));
        var second = received.Single(r => string.Equals(r.Value, "second", StringComparison.Ordinal));

        first.MessageId.Should().MatchRegex($"^nats:{host.StreamName}:1:[0-9]+$");
        first.MessageName.Should().Be(host.MessageName);
        first.CorrelationId.Should().Be(first.MessageId);
        first.ContractVersion.Should().Be("2");
        first.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        second.MessageId.Should().Be(producerId);

        // when: the durable is deleted, so the consumer binds a new one that delivers both stored messages again
        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        await js.DeleteConsumerAsync(host.StreamName, host.DurableName, AbortToken);
        await _WaitUntilAsync(
            async () => await _AcknowledgedThroughAsync(js, host, streamSequence: 2),
            "the rebound durable acknowledged both stored messages"
        );

        // then: the inbox recognised both redeliveries by their identifiers, so no handler ran again. The core commits
        // before it dispatches, so give a wrongly admitted redelivery time to reach the handler.
        await Task.Delay(TimeSpan.FromSeconds(1), AbortToken);
        host.Consumer.Received.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_process_a_new_message_at_a_sequence_a_recreated_stream_reuses()
    {
        // given
        await using var host = await _StartHostAsync();
        await _PublishNativeAsync(host, """{"value":"before"}""", natsMsgId: null);
        await host.Consumer.WaitForCountAsync(1, _Wait, AbortToken);

        // when: the stream is deleted and recreated, so the next message is stored at sequence 1 again
        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        var stream = await js.GetStreamAsync(host.StreamName, cancellationToken: AbortToken);
        var config = stream.Info.Config;
        await js.DeleteStreamAsync(host.StreamName, AbortToken);
        await js.CreateStreamAsync(config, AbortToken);
        await _PublishNativeAsync(host, """{"value":"after"}""", natsMsgId: null);

        // then: its stored time tells it apart from the message the old stream held at the same sequence
        await host.Consumer.WaitForCountAsync(2, _Wait, AbortToken);
        var ids = host.Consumer.Received.Select(r => r.MessageId).ToArray();
        ids.Should().AllSatisfy(id => id.Should().StartWith($"nats:{host.StreamName}:1:"));
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task should_poison_a_raw_message_with_a_malformed_body_without_redelivering_it()
    {
        // given
        await using var host = await _StartHostAsync();
        var producerId = $"producer-{Guid.NewGuid():N}";

        // when
        await _PublishNativeAsync(host, "{not json", producerId);

        // then
        var poisoned = await _WaitForReceivedRowAsync(host, producerId);
        poisoned.StatusName.Should().Be(StatusName.Failed);

        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        await _WaitUntilAsync(
            async () => await _AcknowledgedThroughAsync(js, host, streamSequence: 1),
            "the durable acknowledged the poisoned message"
        );
        var consumer = await js.GetConsumerAsync(host.StreamName, host.DurableName, AbortToken);
        consumer.Info.NumRedelivered.Should().Be(0);
        host.Consumer.Received.Should().BeEmpty();
    }

    private async Task<RawHost> _StartHostAsync()
    {
        var name = $"raw-{Guid.NewGuid():N}";
        var streamName = $"RAW_{Guid.NewGuid():N}";
        var subject = NatsPhysicalAddress.Subject(MessageLane.Queue, name);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<RawRunConsumer>();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseNats(options =>
            {
                options.Servers = fixture.ConnectionString;

                // Limits retention keeps an acknowledged message, so a rebound durable can deliver it again.
                options.Streams.Own(streamName, stream => stream.Subjects(subject));
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
            await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
            var host = new RawHost(
                provider,
                name,
                subject,
                streamName,
                NatsConsumerClient.BuildDurableName(name, name, MessageLane.Queue)
            );

            var js = new NatsJSContext(await fixture.GetConnectionAsync());
            await _WaitUntilAsync(
                async () =>
                {
                    try
                    {
                        _ = await js.GetConsumerAsync(host.StreamName, host.DurableName, AbortToken);
                        return true;
                    }
                    catch (NatsJSApiException e) when (e.Error.Code == 404)
                    {
                        return false;
                    }
                },
                $"durable '{host.DurableName}' exists"
            );

            return host;
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }

    private async Task _PublishNativeAsync(RawHost host, string json, string? natsMsgId)
    {
        var js = new NatsJSContext(await fixture.GetConnectionAsync());
        var opts = natsMsgId is null ? null : new NatsJSPubOpts { MsgId = natsMsgId };

        var ack = await js.PublishAsync(
            host.Subject,
            Encoding.UTF8.GetBytes(json),
            opts: opts,
            cancellationToken: AbortToken
        );

        ack.EnsureSuccess();
    }

    private static async Task<bool> _AcknowledgedThroughAsync(NatsJSContext js, RawHost host, ulong streamSequence)
    {
        try
        {
            var consumer = await js.GetConsumerAsync(host.StreamName, host.DurableName, AbortToken);
            return consumer.Info.AckFloor.StreamSeq >= streamSequence && consumer.Info.NumAckPending == 0;
        }
        catch (NatsJSApiException e) when (e.Error.Code == 404)
        {
            return false;
        }
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
                    && item.StatusName == StatusName.Failed
                );
                return row is not null;
            },
            $"a failed received row for message '{messageId}'"
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
        string subject,
        string streamName,
        string durableName
    ) : IAsyncDisposable
    {
        public IServiceProvider Services => services;

        public RawRunConsumer Consumer { get; } = services.GetRequiredService<RawRunConsumer>();

        public string MessageName => messageName;

        public string Subject => subject;

        public string StreamName => streamName;

        public string DurableName => durableName;

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
                "tests.nats.raw-run",
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
