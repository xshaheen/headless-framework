// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// A non-.NET producer publishes JSON with the Azure client, without the Headless envelope headers, to the queue Headless
/// derives from the message name; a consumer whose contract declares <c>RawPayload()</c> receives it.
/// </summary>
[Collection("AzureServiceBus")]
public sealed class AzureServiceBusRawPayloadTests(AzureServiceBusFixture fixture) : TestBase
{
    // The fixture's queues lock a message for five seconds and the host renews no lock, so a handler that runs longer
    // loses the lock and Azure redelivers the message after the handler already succeeded.
    private static readonly TimeSpan _PastTheLock = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan _Deadline = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task should_run_the_handler_once_when_azure_redelivers_a_headerless_message_it_already_handled()
    {
        // given
        var queueName = $"hfraw-{Guid.NewGuid():N}";
        await fixture.CreateQueueAsync(queueName, AbortToken);
        await using var host = await _StartHostAsync(queueName);
        var consumer = host.GetRequiredService<RawRunConsumer>();
        var deliveries = host.GetRequiredService<NativeDeliveries>();
        consumer.HoldFirstCallFor = _PastTheLock;

        // when
        await _SendAsync(queueName, """{"runId":"r-1","score":7}""");

        // then
        await deliveries.SecondDelivery.Task.WaitAsync(_Deadline, AbortToken);
        await _WaitUntilQueueIsEmptyAsync(queueName);

        var delivered = deliveries.All.ToArray();
        delivered.Select(d => d.SequenceNumber).Distinct().Should().ContainSingle("both deliveries are one message");

        var (nativeMessageId, sequenceNumber, enqueuedTime) = delivered[0];
        var expectedId = string.IsNullOrWhiteSpace(nativeMessageId)
            ? string.Create(CultureInfo.InvariantCulture, $"asb:{queueName}:{sequenceNumber}:{enqueuedTime.UtcTicks}")
            : nativeMessageId;

        consumer.Calls.Should().ContainSingle("the inbox recognizes the redelivery by its derived message id");
        var call = consumer.Calls.Single();
        call.MessageId.Should().Be(expectedId);
        call.Message.Should().Be(new RawRun("r-1", 7));
    }

    [Fact]
    public async Task should_dead_letter_a_headerless_message_whose_json_body_is_malformed_without_redelivery()
    {
        // given
        var queueName = $"hfraw-{Guid.NewGuid():N}";
        await fixture.CreateQueueAsync(queueName, AbortToken);
        await using var host = await _StartHostAsync(queueName);
        var consumer = host.GetRequiredService<RawRunConsumer>();
        var deliveries = host.GetRequiredService<NativeDeliveries>();

        // when
        await _SendAsync(queueName, "{not json");

        // then
        var deadLettered = await _ReceiveDeadLetterAsync(queueName);
        deadLettered.DeadLetterReason.Should().Be("DeserializationFailed");
        (await host.GetRequiredService<IDataStorage>().GetMonitoringApi().GetReceivedFailedCountAsync(AbortToken))
            .Should()
            .Be(1, "the message is kept as poison");
        consumer.Calls.Should().BeEmpty();
        deliveries.All.Should().ContainSingle("a poisoned message is not redelivered");
    }

    private async Task<ServiceProvider> _StartHostAsync(string queueName)
    {
        var connectionString = fixture.ConnectionString;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<RawRunConsumer>();
        services.AddSingleton<NativeDeliveries>();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseAzureServiceBus(options =>
            {
                options.ConnectionString = connectionString;
                options.AutoProvision = false;
                options.MaxAutoLockRenewalDuration = TimeSpan.Zero;

                // Runs once per broker delivery, so it records redeliveries the handler never sees.
                options.CustomHeadersBuilder = static (message, provider) =>
                {
                    provider.GetRequiredService<NativeDeliveries>().Add(message);
                    return [];
                };
            });
            setup.UseInMemoryStorage();
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<RawRun>(queueName).OnQueue(queue => queue.RawPayload());
            messaging.AddModule<RawRunModule>();
        });

        var provider = services.BuildServiceProvider();

        try
        {
            await provider.ApplyMessagingSchemaAsync(AbortToken);
            await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
            return provider;
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }

    private async Task _SendAsync(string queueName, string json)
    {
        await using var client = new ServiceBusClient(fixture.ConnectionString);
        await using var sender = client.CreateSender(queueName);
        await sender.SendMessageAsync(
            new ServiceBusMessage(BinaryData.FromString(json)) { ContentType = "application/json" },
            AbortToken
        );
    }

    private async Task<ServiceBusReceivedMessage> _ReceiveDeadLetterAsync(string queueName)
    {
        await using var client = new ServiceBusClient(fixture.ConnectionString);
        await using var receiver = client.CreateReceiver(
            queueName,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter }
        );

        var message = await receiver.ReceiveMessageAsync(_Deadline, AbortToken);
        message.Should().NotBeNull("the malformed message is dead-lettered");
        await receiver.CompleteMessageAsync(message!, AbortToken);
        return message!;
    }

    private async Task _WaitUntilQueueIsEmptyAsync(string queueName)
    {
        var administration = new ServiceBusAdministrationClient(fixture.ConnectionString);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        deadline.CancelAfter(_Deadline);

        while (true)
        {
            QueueRuntimeProperties properties = await administration.GetQueueRuntimePropertiesAsync(
                queueName,
                deadline.Token
            );

            if (properties.TotalMessageCount == 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), deadline.Token);
        }
    }

    public sealed record RawRun(
        [property: JsonPropertyName("runId")] string RunId,
        [property: JsonPropertyName("score")] int Score
    );

    public sealed class RawRunConsumer : IConsume<RawRun>
    {
        private int _calls;

        public TimeSpan HoldFirstCallFor { get; set; }

        public ConcurrentQueue<(string MessageId, RawRun Message)> Calls { get; } = new();

        public async ValueTask ConsumeAsync(ConsumeContext<RawRun> context, CancellationToken cancellationToken)
        {
            Calls.Enqueue((context.MessageId, context.Message));

            if (Interlocked.Increment(ref _calls) == 1 && HoldFirstCallFor > TimeSpan.Zero)
            {
                // Real time: the broker's lock expires on its own clock, and only a handler that outlives it makes
                // Azure redeliver.
                await Task.Delay(HoldFirstCallFor, CancellationToken.None);
            }
        }
    }

    public sealed class NativeDeliveries
    {
        public ConcurrentQueue<(string? MessageId, long SequenceNumber, DateTimeOffset EnqueuedTime)> All { get; } =
            new();

        public TaskCompletionSource SecondDelivery { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Add(ServiceBusReceivedMessage message)
        {
            All.Enqueue((message.MessageId, message.SequenceNumber, message.EnqueuedTime));

            if (All.Count >= 2)
            {
                SecondDelivery.TrySetResult();
            }
        }
    }

    public sealed class RawRunModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddQueueConsumer<RawRunConsumer, RawRun>(
                "tests.asb.raw-run",
                TestConsumerDispatch.FromServices<RawRunConsumer, RawRun>()
            );
    }
}
