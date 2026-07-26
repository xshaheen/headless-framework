// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Registration;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Internal;

public sealed class MessagePublisherDeliveryTests : TestBase
{
    [Fact]
    public async Task should_send_auto_directly_without_coordination_or_storage_side_effects()
    {
        await using var harness = _CreateHarness();

        await harness.Publisher.PublishAsync(MessageLane.Bus, new DeliveryMessage("direct"), options: null, AbortToken);

        harness.TransportLanes.Should().ContainSingle().Which.Should().Be(MessageLane.Bus);
        var sent = harness.TransportMessages.Should().ContainSingle().Which;
        sent.Headers[Headers.RequestedDeliveryMode].Should().Be(nameof(DeliveryMode.Auto));
        sent.Headers[Headers.ResolvedDeliveryMode].Should().Be(nameof(DeliveryMode.TransportDirect));
        await harness
            .Storage.DidNotReceive()
            .StoreMessageAsync(
                Arg.Any<string>(),
                Arg.Any<MediumMessage>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_capture_durable_without_coordination_and_preserve_queue_lane()
    {
        await using var harness = _CreateHarness();
        MediumMessage? stored = null;
        harness
            .Storage.StoreMessageAsync(
                Arg.Any<string>(),
                Arg.Any<MediumMessage>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                stored = call.ArgAt<MediumMessage>(1);
                stored.StorageId = Guid.NewGuid();
                return ValueTask.FromResult(stored);
            });

        await harness.Publisher.PublishAsync(
            MessageLane.Queue,
            new DeliveryMessage("durable"),
            new EnqueueOptions { DeliveryMode = DeliveryMode.Durable },
            AbortToken
        );

        harness.TransportLanes.Should().BeEmpty();
        stored.Should().NotBeNull();
        stored!.Lane.Should().Be(MessageLane.Queue);
        stored.Origin.Headers[Headers.RequestedDeliveryMode].Should().Be(nameof(DeliveryMode.Durable));
        stored.Origin.Headers[Headers.ResolvedDeliveryMode].Should().Be(nameof(DeliveryMode.Durable));
        await harness.Dispatcher.Received(1).EnqueueToPublish(stored, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reject_delayed_transport_direct_before_any_side_effect()
    {
        await using var harness = _CreateHarness();

        var act = () =>
            harness.Publisher.PublishAsync(
                MessageLane.Bus,
                new DeliveryMessage("invalid"),
                new PublishOptions { DeliveryMode = DeliveryMode.TransportDirect, Delay = TimeSpan.FromMinutes(1) },
                AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*TransportDirect*delay*");
        harness.TransportLanes.Should().BeEmpty();
        await harness
            .Storage.DidNotReceive()
            .StoreMessageAsync(
                Arg.Any<string>(),
                Arg.Any<MediumMessage>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<CancellationToken>()
            );
        await harness.Dispatcher.DidNotReceiveWithAnyArgs().EnqueueToScheduler(default!, default, default, default);
    }

    [Fact]
    public async Task should_schedule_auto_delay_at_the_single_resolved_timestamp()
    {
        var now = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        await using var harness = _CreateHarness(timeProvider);
        var stored = new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(new Dictionary<string, string?>(StringComparer.Ordinal), value: null),
            Content = "{}",
            Lane = MessageLane.Bus,
        };
        harness
            .Storage.StoreMessageAsync(
                Arg.Any<string>(),
                Arg.Any<MediumMessage>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(stored));

        await harness.Publisher.PublishAsync(
            MessageLane.Bus,
            new DeliveryMessage("delayed"),
            new PublishOptions { Delay = TimeSpan.FromMinutes(5) },
            AbortToken
        );

        harness.TransportLanes.Should().BeEmpty();
        await harness
            .Dispatcher.Received(1)
            .EnqueueToScheduler(stored, now.AddMinutes(5), transaction: null, Arg.Any<CancellationToken>());
    }

    private static MessagePublisherHarness _CreateHarness(TimeProvider? timeProvider = null)
    {
        timeProvider ??= TimeProvider.System;
        var storage = Substitute.For<IDataStorage>();
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher
            .EnqueueToPublish(Arg.Any<MediumMessage>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        dispatcher
            .EnqueueToScheduler(
                Arg.Any<MediumMessage>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        var options = Options.Create(new MessagingOptions());
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(DeliveryMessage), "delivery.message");
        var requestFactory = new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            timeProvider,
            options,
            registry,
            new NullCurrentTenant()
        );
        var services = new ServiceCollection().BuildServiceProvider();
        var pipeline = new PublishMiddlewarePipeline(services);
        var writer = new OutboxMessageWriter(
            storage,
            dispatcher,
            requestFactory,
            new MessagingNullCommitCoordinator(),
            pipeline,
            timeProvider,
            options,
            NullLogger<MessageOutboxBuffer>.Instance
        );
        var capabilities = MessagingCapabilityModel.Compose([
            MessagingProviderCapabilities.Transport(
                "TestTransport",
                [MessageLane.Bus, MessageLane.Queue],
                supportsIndependentLaneTopology: true
            ),
            MessagingProviderCapabilities.Storage(
                "TestStorage",
                [MessageLane.Bus, MessageLane.Queue],
                supportsDelayedScheduling: true
            ),
        ]);
        var transportLanes = new List<MessageLane>();
        var transportMessages = new List<TransportMessage>();
        var transports = new Dictionary<MessageLane, ITransport>
        {
            [MessageLane.Bus] = new RecordingTransport(MessageLane.Bus, transportLanes, transportMessages),
            [MessageLane.Queue] = new RecordingTransport(MessageLane.Queue, transportLanes, transportMessages),
        };
        var publisher = new MessagePublisher(
            new JsonUtf8Serializer(options),
            lane => transports[lane],
            requestFactory,
            pipeline,
            timeProvider,
            capabilities,
            new MessagingNullCommitCoordinator(),
            static () => null,
            () => writer
        );

        return new MessagePublisherHarness(publisher, storage, dispatcher, transportLanes, transportMessages, services);
    }

    private sealed record DeliveryMessage(string Value);

    private sealed class RecordingTransport(MessageLane lane, List<MessageLane> lanes, List<TransportMessage> messages)
        : ITransport
    {
        public BrokerAddress BrokerAddress { get; } = new("Test", "localhost");

        public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
        {
            lanes.Add(lane);
            messages.Add(message);
            return Task.FromResult(OperateResult.Success);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed record MessagePublisherHarness(
        MessagePublisher Publisher,
        IDataStorage Storage,
        IDispatcher Dispatcher,
        List<MessageLane> TransportLanes,
        List<TransportMessage> TransportMessages,
        ServiceProvider Services
    ) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            return Services.DisposeAsync();
        }
    }
}
