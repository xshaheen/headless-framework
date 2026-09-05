// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.CommitCoordination;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Internal;

public sealed class MessagePublisherDeliveryTests : TestBase
{
    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_preserve_same_affinity_in_direct_transport_and_committed_outbox(MessageLane lane)
    {
        await using var harness = _CreateHarness();
        var serializer = new JsonUtf8Serializer(Options.Create(new MessagingOptions()));
        harness
            .Storage.StoreMessageAsync(
                Arg.Any<string>(),
                Arg.Any<MediumMessage>(),
                Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var stored = call.ArgAt<MediumMessage>(1);
                stored.StorageId = Guid.NewGuid();
                stored.Content = serializer.Serialize(stored.Origin);
                stored.Origin = serializer.Deserialize(stored.Content)!;
                return ValueTask.FromResult(stored);
            });
        MessageOptions direct =
            lane == MessageLane.Bus
                ? new PublishOptions
                {
                    MessageName = "delivery.message",
                    RoutingAffinityKey = "order-42",
                    DeliveryMode = DeliveryMode.TransportDirect,
                }
                : new EnqueueOptions
                {
                    MessageName = "delivery.message",
                    RoutingAffinityKey = "order-42",
                    DeliveryMode = DeliveryMode.TransportDirect,
                };

        await harness.Publisher.PublishAsync(lane, new DeliveryMessage("payload"), direct, AbortToken);
        await harness.Publisher.PublishAsync(
            lane,
            new DeliveryMessage("payload"),
            direct with
            {
                DeliveryMode = DeliveryMode.Durable,
            },
            AbortToken
        );

        var sent = harness.TransportMessages.Should().ContainSingle().Subject;
        var committed = harness.Dispatcher.CommittedMessages.Should().ContainSingle().Subject;
        sent.RoutingAffinityKey.Should().Be("order-42");
        committed.RoutingAffinityKey.Should().Be(sent.RoutingAffinityKey);
        committed.Lane.Should().Be(lane);
        var restored = await serializer.SerializeToTransportMessageAsync(committed.Origin, AbortToken);
        restored.RoutingAffinityKey.Should().Be(sent.RoutingAffinityKey);
    }

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
    public async Task should_send_transport_direct_through_incompatible_coordination_without_storage_side_effects()
    {
        var stack = new CommitScopeStack();
        await using var scope = new CommitScopeFactory(stack).Begin(new EmptyServiceProvider(), []);
        await using var harness = _CreateHarness(
            currentCommitCoordinator: stack,
            coordinationResolver: static () => new IncompatibleCoordinationResolver()
        );

        await harness.Publisher.PublishAsync(
            MessageLane.Bus,
            new DeliveryMessage("direct"),
            new PublishOptions { DeliveryMode = DeliveryMode.TransportDirect },
            AbortToken
        );

        harness.TransportLanes.Should().ContainSingle().Which.Should().Be(MessageLane.Bus);
        var sent = harness.TransportMessages.Should().ContainSingle().Which;
        sent.Headers[Headers.RequestedDeliveryMode].Should().Be(nameof(DeliveryMode.TransportDirect));
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
        var storage = harness.Storage;
        MediumMessage? stored = null;
        storage
            .StoreMessageAsync(
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
        harness.Dispatcher.CommittedMessages.Should().ContainSingle().Which.Should().BeSameAs(stored);
        harness.Dispatcher.PublishCalls.Should().Be(0);
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
        harness.Dispatcher.SchedulerCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_schedule_auto_delay_at_the_single_resolved_timestamp()
    {
        var now = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        await using var harness = _CreateHarness(timeProvider);
        var storage = harness.Storage;
        var stored = new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(new Dictionary<string, string?>(StringComparer.Ordinal), value: null),
            Content = "{}",
            Lane = MessageLane.Bus,
        };
        storage
            .StoreScheduledMessageAsync(
                Arg.Any<string>(),
                Arg.Any<MediumMessage>(),
                now.AddMinutes(5),
                transaction: null,
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
        harness.Dispatcher.CommittedDelayedMessages.Should().ContainSingle().Which.Should().BeSameAs(stored);
        harness.Dispatcher.SchedulerCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_cancel_direct_transport_when_publish_timeout_elapses()
    {
        // given
        var timeProvider = new FakeTimeProvider();
#pragma warning disable CA2000 // MessagePublisherHarness takes ownership of the supplied transport and disposes it with the publisher dependencies.
        var transport = new BlockingTransport();
#pragma warning restore CA2000
        var timeout = TimeSpan.FromSeconds(5);
        await using var harness = _CreateHarness(timeProvider, timeout, transport);

        // when
        var publishTask = harness.Publisher.PublishAsync(
            MessageLane.Bus,
            new DeliveryMessage("timeout"),
            options: null,
            AbortToken
        );
        await transport.Started.Task.WaitAsync(AbortToken);
        timeProvider.Advance(timeout);
        var act = async () => await publishTask;

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_preserve_caller_token_when_direct_transport_is_canceled_by_caller()
    {
        // given
        var timeProvider = new FakeTimeProvider();
#pragma warning disable CA2000 // MessagePublisherHarness takes ownership of the supplied transport and disposes it with the publisher dependencies.
        var transport = new BlockingTransport();
#pragma warning restore CA2000
        await using var harness = _CreateHarness(timeProvider, TimeSpan.FromHours(1), transport);
        using var callerCts = new CancellationTokenSource();

        // when
        var publishTask = harness.Publisher.PublishAsync(
            MessageLane.Bus,
            new DeliveryMessage("caller-canceled"),
            options: null,
            callerCts.Token
        );
        await transport.Started.Task.WaitAsync(AbortToken);
        await callerCts.CancelAsync();
        var act = async () => await publishTask;

        // then
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(callerCts.Token);
    }

    [Fact]
    public async Task should_start_transport_timeout_only_after_serialization_completes()
    {
        // given
        var timeProvider = new FakeTimeProvider();
        var serializer = new BlockingTransportSerializer();
#pragma warning disable CA2000 // MessagePublisherHarness takes ownership of the supplied transport and disposes it with the publisher dependencies.
        var transport = new BlockingTransport();
#pragma warning restore CA2000
        var timeout = TimeSpan.FromSeconds(5);
        await using var harness = _CreateHarness(timeProvider, timeout, transport, serializer);

        // when
        var publishTask = harness.Publisher.PublishAsync(
            MessageLane.Bus,
            new DeliveryMessage("slow-serialization"),
            options: null,
            AbortToken
        );
        await serializer.Started.Task.WaitAsync(AbortToken);
        timeProvider.Advance(timeout);

        // then
        publishTask.IsCompleted.Should().BeFalse("serialization uses only the caller cancellation token");

        serializer.Release();
        await transport.Started.Task.WaitAsync(AbortToken);
        publishTask.IsCompleted.Should().BeFalse("the transport receives a fresh timeout budget");

        timeProvider.Advance(timeout);
        var act = async () => await publishTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static MessagePublisherHarness _CreateHarness(
        TimeProvider? timeProvider = null,
        TimeSpan? transportPublishTimeout = null,
        ITransport? busTransport = null,
        ISerializer? serializer = null,
        ICurrentCommitCoordinator? currentCommitCoordinator = null,
        Func<IDeliveryCoordinationResolver?>? coordinationResolver = null
    )
    {
        timeProvider ??= TimeProvider.System;
        var storage = Substitute.For<IDataStorage>();
#pragma warning disable CA2000 // MessagePublisherHarness owns the dispatcher and disposes it after all publisher assertions complete.
        var dispatcher = new RecordingCommittedDispatcher();
#pragma warning restore CA2000

        var options = Options.Create(new MessagingOptions());
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(DeliveryMessage), "delivery.message");
        var capabilities = MessagingCapabilityModel.Compose([
            MessagingProviderCapabilities.Transport(
                "TestTransport",
                [MessageLane.Bus, MessageLane.Queue],
                supportsIndependentLaneTopology: true,
                routingAffinityRoutes:
                [
                    new(MessageLane.Bus, "delivery.message", new MessagingRoutingAffinityMapping("native-key")),
                    new(MessageLane.Queue, "delivery.message", new MessagingRoutingAffinityMapping("native-key")),
                ]
            ),
            MessagingProviderCapabilities.Storage(
                "TestStorage",
                [MessageLane.Bus, MessageLane.Queue],
                supportsDelayedScheduling: true,
                inboxCapability: MessagingInboxCapabilityTier.Transactional
            ),
        ]);
        var requestFactory = new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            timeProvider,
            options,
            registry,
            new NullCurrentTenant(),
            new MessageMetadataRegistry([
                new Headless.Messaging.Registration.MessageRegistration(
                    typeof(DeliveryMessage),
                    MessageLane.Bus,
                    "delivery.message",
                    null,
                    new Dictionary<Type, object>(),
                    []
                ),
                new Headless.Messaging.Registration.MessageRegistration(
                    typeof(DeliveryMessage),
                    MessageLane.Queue,
                    "delivery.message",
                    null,
                    new Dictionary<Type, object>(),
                    []
                ),
            ]),
            capabilityGate: capabilities
        );
        var services = new ServiceCollection().BuildServiceProvider();
        var pipeline = new PublishMiddlewarePipeline(services);
        var writer = new OutboxMessageWriter(storage, dispatcher, timeProvider);

        var transportLanes = new List<MessageLane>();
        var transportMessages = new List<TransportMessage>();
        var transports = new Dictionary<MessageLane, ITransport>
        {
            [MessageLane.Bus] =
                busTransport ?? new RecordingTransport(MessageLane.Bus, transportLanes, transportMessages),
            [MessageLane.Queue] = new RecordingTransport(MessageLane.Queue, transportLanes, transportMessages),
        };
        var publisher = new MessagePublisher(
            serializer ?? new JsonUtf8Serializer(options),
            lane => transports[lane],
            requestFactory,
            pipeline,
            timeProvider,
            capabilities,
            currentCommitCoordinator ?? new MessagingNullCommitCoordinator(),
            coordinationResolver ?? (static () => null),
            () => writer,
            telemetry: null,
            transportPublishTimeout
        );

        return new MessagePublisherHarness(
            publisher,
            storage,
            dispatcher,
            transportLanes,
            transportMessages,
            [.. transports.Values],
            services
        );
    }

    private sealed record DeliveryMessage(string Value);

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    private sealed class IncompatibleCoordinationResolver : IDeliveryCoordinationResolver
    {
        public DeliveryCoordination Resolve(ICommitCoordinator coordinator)
        {
            return DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.MissingRelationalCapability);
        }
    }

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

    private sealed class BlockingTransport : ITransport
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BrokerAddress BrokerAddress { get; } = new("Test", "localhost");

        public async Task<OperateResult> SendAsync(
            TransportMessage message,
            CancellationToken cancellationToken = default
        )
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return OperateResult.Success;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingTransportSerializer : ISerializer
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Serialize(Message message)
        {
            throw new NotSupportedException();
        }

        public async ValueTask<TransportMessage> SerializeToTransportMessageAsync(
            Message message,
            CancellationToken cancellationToken = default
        )
        {
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new TransportMessage(message.Headers, body: null);
        }

        public Message? Deserialize(string json)
        {
            throw new NotSupportedException();
        }

        public ValueTask<Message> DeserializeAsync(
            TransportMessage transportMessage,
            Type? valueType,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotSupportedException();
        }

        public object? Deserialize(object value, Type valueType)
        {
            throw new NotSupportedException();
        }

        public bool IsJsonType(object jsonObject)
        {
            return false;
        }

        internal void Release()
        {
            _release.TrySetResult();
        }
    }

    private sealed class RecordingCommittedDispatcher
        : IDispatcher,
            ICommittedMessageDispatcher,
            ICommittedDelayedMessageDispatcher
    {
        public List<MediumMessage> CommittedMessages { get; } = [];

        public List<MediumMessage> CommittedDelayedMessages { get; } = [];

        public int PublishCalls { get; private set; }

        public int SchedulerCalls { get; private set; }

        public void EnqueueCommittedMessage(MediumMessage message)
        {
            CommittedMessages.Add(message);
        }

        public void EnqueueCommittedDelayedMessage(MediumMessage message)
        {
            CommittedDelayedMessages.Add(message);
        }

        public ValueTask EnqueueToPublish(MediumMessage message, CancellationToken cancellationToken = default)
        {
            PublishCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask EnqueueToExecute(
            MediumMessage message,
            ConsumerExecutorDescriptor? descriptor = null,
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.CompletedTask;
        }

        public Task EnqueueToScheduler(
            MediumMessage message,
            DateTimeOffset publishTime,
            System.Data.Common.DbTransaction? transaction = null,
            CancellationToken cancellationToken = default
        )
        {
            SchedulerCalls++;
            return Task.CompletedTask;
        }

        public ValueTask StartAsync(CancellationToken stoppingToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed record MessagePublisherHarness(
        MessagePublisher Publisher,
        IDataStorage Storage,
        RecordingCommittedDispatcher Dispatcher,
        List<MessageLane> TransportLanes,
        List<TransportMessage> TransportMessages,
        IReadOnlyCollection<ITransport> Transports,
        ServiceProvider Services
    ) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            foreach (var transport in Transports)
            {
                await transport.DisposeAsync().ConfigureAwait(false);
            }

            await Dispatcher.DisposeAsync().ConfigureAwait(false);
            await Services.DisposeAsync().ConfigureAwait(false);
        }
    }
}
