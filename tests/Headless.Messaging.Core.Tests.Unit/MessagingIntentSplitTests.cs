// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tests.Helpers;

namespace Tests;

public sealed class MessagingIntentSplitTests : TestBase
{
    [Fact]
    public void intent_type_storage_values_should_be_stable()
    {
        // Persistence rows + on-wire serializations rely on these numeric values. Changing them is
        // a breaking change for any drained inbox/outbox row at-rest. Pin them explicitly.
        Assert.Equal(0, (int)IntentType.Bus);
        Assert.Equal(1, (int)IntentType.Queue);
    }

    [Fact]
    public void for_message_on_bus_should_stamp_bus_intent()
    {
        var services = new ServiceCollection();

        services.AddHeadlessMessaging(setup =>
            setup.ForMessage<TestMessage>(message => message.MessageName("events.orders").OnBus<TestBusConsumer>())
        );

        var metadata = services.BuildServiceProvider().GetDrainedConsumerRegistry().GetAll().Single();

        metadata.IntentType.Should().Be(IntentType.Bus);
        metadata.MessageName.Should().Be("events.orders");
    }

    [Fact]
    public void for_message_on_queue_should_stamp_queue_intent()
    {
        var services = new ServiceCollection();

        services.AddHeadlessMessaging(setup =>
            setup.ForMessage<TestMessage>(message => message.MessageName("jobs.orders").OnQueue<TestQueueConsumer>())
        );

        var metadata = services.BuildServiceProvider().GetDrainedConsumerRegistry().GetAll().Single();

        metadata.IntentType.Should().Be(IntentType.Queue);
        metadata.MessageName.Should().Be("jobs.orders");
    }

    [Fact]
    public void consumer_registry_should_allow_same_topic_group_across_different_intents()
    {
        var registry = new ConsumerRegistry();

        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestBusConsumer),
                "orders",
                "workers",
                1,
                IntentType: IntentType.Bus
            )
        );
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestQueueConsumer),
                "orders",
                "workers",
                1,
                IntentType: IntentType.Queue
            )
        );

        registry.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public async Task bootstrap_should_fail_when_queue_consumer_has_no_queue_transport()
    {
        var services = new ServiceCollection();
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestQueueConsumer),
                "jobs.orders",
                "workers",
                1,
                IntentType: IntentType.Queue
            )
        );

        services.AddSingleton(new MessagingMarkerService("Messaging"));
        services.AddSingleton(new MessageQueueMarkerService("TestTransport"));
        services.AddSingleton(new MessageStorageMarkerService("TestStorage"));
        services.AddSingleton<IConsumerRegistry>(registry);
        services.AddSingleton(registry);

        await using var provider = services.BuildServiceProvider();

        await using var bootstrapper = new Bootstrapper(
            [],
            new NoOpStorageInitializer(),
            provider,
            Options.Create(new MessagingOptions()),
            NullLogger<IBootstrapper>.Instance
        );

        var act = () => bootstrapper.BootstrapAsync(AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IQueueTransport*available*");
    }

    [Fact]
    public async Task bootstrap_should_fail_when_bus_consumer_has_no_bus_transport()
    {
        var services = new ServiceCollection();
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestBusConsumer),
                "events.orders",
                "workers",
                1,
                IntentType: IntentType.Bus
            )
        );

        // Register only the high-level markers; no IBusTransport and no ITransport so the legacy
        // adapter cannot kick in either. The bootstrapper must surface the per-intent friendly
        // message naming ForMessage<...>.
        services.AddSingleton(new MessagingMarkerService("Messaging"));
        services.AddSingleton(new MessageQueueMarkerService("TestTransport"));
        services.AddSingleton(new MessageStorageMarkerService("TestStorage"));
        services.AddSingleton<IConsumerRegistry>(registry);
        services.AddSingleton(registry);

        await using var provider = services.BuildServiceProvider();

        await using var bootstrapper = new Bootstrapper(
            [],
            new NoOpStorageInitializer(),
            provider,
            Options.Create(new MessagingOptions()),
            NullLogger<IBootstrapper>.Instance
        );

        var act = () => bootstrapper.BootstrapAsync(AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IBusTransport*available*");
    }

    [Fact]
    public async Task bootstrap_should_not_require_bus_transport_for_queue_only_consumer()
    {
        var services = new ServiceCollection();
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestQueueConsumer),
                "jobs.orders",
                "workers",
                1,
                IntentType: IntentType.Queue
            )
        );

        services.AddSingleton(new MessagingMarkerService("Messaging"));
        services.AddSingleton(new MessageQueueMarkerService("QueueOnlyTransport"));
        services.AddSingleton(new MessageStorageMarkerService("TestStorage"));
        services.AddSingleton<IConsumerRegistry>(registry);
        services.AddSingleton(registry);
        services.AddSingleton(Substitute.For<IQueueTransport>());
        services.AddSingleton(Substitute.For<IBus>());
        services.AddSingleton(Substitute.For<IOutboxBus>());

        await using var provider = services.BuildServiceProvider();

        await using var bootstrapper = new Bootstrapper(
            [],
            new NoOpStorageInitializer(),
            provider,
            Options.Create(new MessagingOptions()),
            NullLogger<IBootstrapper>.Instance
        );

        await bootstrapper.BootstrapAsync(AbortToken);
    }

    [Fact]
    public void add_headless_messaging_should_register_only_queue_publishers_for_queue_only_transport()
    {
        var services = new ServiceCollection();

        services.AddHeadlessMessaging(setup => setup.RegisterExtension(new QueueOnlyMessagingExtension()));

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IQueue));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IOutboxQueue));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IBus));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IOutboxBus));
    }

    [Fact]
    public void add_headless_messaging_should_register_only_bus_publishers_for_bus_only_transport()
    {
        var services = new ServiceCollection();

        services.AddHeadlessMessaging(setup => setup.RegisterExtension(new BusOnlyMessagingExtension()));

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IBus));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IOutboxBus));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IQueue));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IOutboxQueue));
    }

    [Fact]
    public async Task bootstrap_should_fail_when_bus_publisher_is_registered_without_bus_transport()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup => setup.RegisterExtension(new QueueOnlyMessagingExtension()));
        services.AddSingleton(Substitute.For<IBus>());

        await using var provider = services.BuildServiceProvider();

        await using var bootstrapper = new Bootstrapper(
            [],
            new NoOpStorageInitializer(),
            provider,
            Options.Create(new MessagingOptions()),
            NullLogger<IBootstrapper>.Instance
        );

        var act = () => bootstrapper.BootstrapAsync(AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IBusTransport*available*");
    }

    [Fact]
    public async Task bootstrap_should_fail_when_queue_publisher_is_registered_without_queue_transport()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup => setup.RegisterExtension(new BusOnlyMessagingExtension()));
        services.AddSingleton(Substitute.For<IQueue>());

        await using var provider = services.BuildServiceProvider();

        await using var bootstrapper = new Bootstrapper(
            [],
            new NoOpStorageInitializer(),
            provider,
            Options.Create(new MessagingOptions()),
            NullLogger<IBootstrapper>.Instance
        );

        var act = () => bootstrapper.BootstrapAsync(AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IQueueTransport*available*");
    }

    [Fact]
    public async Task bus_publish_should_throw_publisher_sent_failed_when_transport_reports_failure()
    {
        // given
        await using var transport = new FailingBusTransport();
        var bus = _CreateBus(transport);

        // when
        var act = () => bus.PublishAsync(new TestMessage(), cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<PublisherSentFailedException>();
    }

    [Fact]
    public async Task bus_publish_should_request_bus_intent_from_factory_and_trace_prepared_intent()
    {
        // given
        var publishRequestFactory = Substitute.For<IMessagePublishRequestFactory>();
        publishRequestFactory
            .Create(Arg.Any<TestMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<TimeSpan?>(), IntentType.Bus)
            .Returns(_CreatePreparedPublishMessage("events.prepared", IntentType.Bus));

        await using var transport = new CapturingBusTransport();
        using var diagnostics = new CapturingDiagnosticObserver("events.prepared");
        var bus = _CreateBus(transport, publishRequestFactory);

        // when
        await bus.PublishAsync(new TestMessage(), cancellationToken: AbortToken);

        // then
        _ = publishRequestFactory
            .Received(1)
            .Create(
                Arg.Any<TestMessage>(),
                Arg.Any<PublishOptions?>(),
                Arg.Is<TimeSpan?>(delay => delay == null),
                IntentType.Bus
            );
        transport.LastMessage!.Value.GetName().Should().Be("events.prepared");
        diagnostics
            .BeforePublishData.Should()
            .BeOfType<MessageEventDataPubSend>()
            .Which.IntentType.Should()
            .Be(IntentType.Bus);
    }

    [Fact]
    public async Task queue_enqueue_should_throw_publisher_sent_failed_when_transport_reports_failure()
    {
        // given
        await using var transport = new FailingQueueTransport();
        var queue = _CreateQueue(transport);

        // when
        var act = () => queue.EnqueueAsync(new TestMessage(), cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<PublisherSentFailedException>();
    }

    [Fact]
    public async Task queue_enqueue_should_request_queue_intent_from_factory_and_trace_prepared_intent()
    {
        // given
        var publishRequestFactory = Substitute.For<IMessagePublishRequestFactory>();
        publishRequestFactory
            .Create(Arg.Any<TestMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<TimeSpan?>(), IntentType.Queue)
            .Returns(_CreatePreparedPublishMessage("jobs.prepared", IntentType.Queue));

        await using var transport = new CapturingQueueTransport();
        using var diagnostics = new CapturingDiagnosticObserver("jobs.prepared");
        var queue = _CreateQueue(transport, publishRequestFactory);

        // when
        await queue.EnqueueAsync(new TestMessage(), cancellationToken: AbortToken);

        // then
        _ = publishRequestFactory
            .Received(1)
            .Create(
                Arg.Any<TestMessage>(),
                Arg.Any<PublishOptions?>(),
                Arg.Is<TimeSpan?>(delay => delay == null),
                IntentType.Queue
            );
        transport.LastMessage!.Value.GetName().Should().Be("jobs.prepared");
        diagnostics
            .BeforePublishData.Should()
            .BeOfType<MessageEventDataPubSend>()
            .Which.IntentType.Should()
            .Be(IntentType.Queue);
    }

    private static IBus _CreateBus(IBusTransport transport, IMessagePublishRequestFactory? publishRequestFactory = null)
    {
        var optionsAccessor = Options.Create(new MessagingOptions());
        var serializer = new JsonUtf8Serializer(optionsAccessor);
        publishRequestFactory ??= _CreatePublishRequestFactory(optionsAccessor);
        var pipeline = new PublishMiddlewarePipeline(new ServiceCollection().BuildServiceProvider());

        return new Bus(serializer, transport, publishRequestFactory, pipeline, TimeProvider.System);
    }

    private static IQueue _CreateQueue(
        IQueueTransport transport,
        IMessagePublishRequestFactory? publishRequestFactory = null
    )
    {
        var optionsAccessor = Options.Create(new MessagingOptions());
        var serializer = new JsonUtf8Serializer(optionsAccessor);
        publishRequestFactory ??= _CreatePublishRequestFactory(optionsAccessor);
        var pipeline = new PublishMiddlewarePipeline(new ServiceCollection().BuildServiceProvider());

        return new Queue(serializer, transport, publishRequestFactory, pipeline, TimeProvider.System);
    }

    private static IMessagePublishRequestFactory _CreatePublishRequestFactory(
        IOptions<MessagingOptions> optionsAccessor
    ) =>
        new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            optionsAccessor,
            new ConsumerRegistry(),
            new NullCurrentTenant()
        );

    private static PreparedPublishMessage _CreatePreparedPublishMessage(string messageName, IntentType intentType) =>
        new()
        {
            MessageName = messageName,
            PublishAt = DateTime.UtcNow,
            Message = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = Guid.NewGuid().ToString(),
                    [Headers.MessageName] = messageName,
                },
                new TestMessage()
            ),
            IntentType = intentType,
        };

    [Fact]
    public void descriptor_comparer_should_treat_bus_and_queue_with_same_topic_group_as_distinct()
    {
        var comparer = new ConsumerExecutorDescriptorComparer(NullLogger<ConsumerExecutorDescriptorComparer>.Instance);
        var implTypeInfo = typeof(TestBusConsumer).GetTypeInfo();
        var methodInfo = typeof(TestBusConsumer).GetMethod(
            nameof(TestBusConsumer.ConsumeAsync),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            [typeof(ConsumeContext<TestMessage>), typeof(CancellationToken)]
        )!;

        var busDescriptor = new ConsumerExecutorDescriptor
        {
            MethodInfo = methodInfo,
            ImplTypeInfo = implTypeInfo,
            MessageName = "orders.created",
            GroupName = "workers",
            IntentType = IntentType.Bus,
        };
        var queueDescriptor = new ConsumerExecutorDescriptor
        {
            MethodInfo = methodInfo,
            ImplTypeInfo = implTypeInfo,
            MessageName = "orders.created",
            GroupName = "workers",
            IntentType = IntentType.Queue,
        };

        comparer.Equals(busDescriptor, queueDescriptor).Should().BeFalse();
        comparer.GetHashCode(busDescriptor).Should().NotBe(comparer.GetHashCode(queueDescriptor));
    }

    [Fact]
    public void descriptor_comparer_should_treat_same_topic_group_and_intent_as_equal()
    {
        var comparer = new ConsumerExecutorDescriptorComparer(NullLogger<ConsumerExecutorDescriptorComparer>.Instance);
        var implTypeInfo = typeof(TestBusConsumer).GetTypeInfo();
        var methodInfo = typeof(TestBusConsumer).GetMethod(
            nameof(TestBusConsumer.ConsumeAsync),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            [typeof(ConsumeContext<TestMessage>), typeof(CancellationToken)]
        )!;

        var first = new ConsumerExecutorDescriptor
        {
            MethodInfo = methodInfo,
            ImplTypeInfo = implTypeInfo,
            MessageName = "orders.created",
            GroupName = "workers",
            IntentType = IntentType.Bus,
        };
        var second = new ConsumerExecutorDescriptor
        {
            MethodInfo = methodInfo,
            ImplTypeInfo = implTypeInfo,
            MessageName = "orders.created",
            GroupName = "workers",
            IntentType = IntentType.Bus,
        };

        comparer.Equals(first, second).Should().BeTrue();
        comparer.GetHashCode(first).Should().Be(comparer.GetHashCode(second));
    }

    private sealed record TestMessage;

    private sealed class TestBusConsumer : IConsume<TestMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<TestMessage> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class TestQueueConsumer : IConsume<TestMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<TestMessage> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class NoOpStorageInitializer : IStorageInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public string GetPublishedTableName() => "published";

        public string GetReceivedTableName() => "received";
    }

    private sealed class QueueOnlyMessagingExtension : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("QueueOnly"));
            services.AddSingleton(new MessageStorageMarkerService("TestStorage"));
            services.AddSingleton<IStorageInitializer, NoOpStorageInitializer>();
            services.AddSingleton<IQueueTransport, CapturingQueueTransport>();
        }
    }

    private sealed class BusOnlyMessagingExtension : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("BusOnly"));
            services.AddSingleton(new MessageStorageMarkerService("TestStorage"));
            services.AddSingleton<IStorageInitializer, NoOpStorageInitializer>();
            services.AddSingleton<IBusTransport, CapturingBusTransport>();
        }
    }

    private sealed class FailingBusTransport : IBusTransport
    {
        public BrokerAddress BrokerAddress { get; } = new("Test", "localhost");

        public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperateResult.Failed(new Exception("bus boom")));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingQueueTransport : IQueueTransport
    {
        public BrokerAddress BrokerAddress { get; } = new("Test", "localhost");

        public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperateResult.Failed(new Exception("queue boom")));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CapturingBusTransport : IBusTransport
    {
        public BrokerAddress BrokerAddress { get; } = new("Test", "localhost");

        public TransportMessage? LastMessage { get; private set; }

        public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
        {
            LastMessage = message;
            return Task.FromResult(OperateResult.Success);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CapturingQueueTransport : IQueueTransport
    {
        public BrokerAddress BrokerAddress { get; } = new("Test", "localhost");

        public TransportMessage? LastMessage { get; private set; }

        public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
        {
            LastMessage = message;
            return Task.FromResult(OperateResult.Success);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CapturingDiagnosticObserver
        : IObserver<DiagnosticListener>,
            IObserver<KeyValuePair<string, object?>>,
            IDisposable
    {
        private readonly IDisposable _allListenersSubscription;
        private readonly string _expectedMessageName;
        private IDisposable? _listenerSubscription;

        public object? BeforePublishData { get; private set; }

        public CapturingDiagnosticObserver(string expectedMessageName)
        {
            _expectedMessageName = expectedMessageName;
            _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public void OnNext(DiagnosticListener value)
        {
            if (
                string.Equals(
                    value.Name,
                    MessageDiagnosticListenerNames.DiagnosticListenerName,
                    StringComparison.Ordinal
                )
            )
            {
                _listenerSubscription = value.Subscribe(this, _IsBeforePublish);
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (
                value.Value is MessageEventDataPubSend eventData
                && string.Equals(eventData.TransportMessage.GetName(), _expectedMessageName, StringComparison.Ordinal)
            )
            {
                BeforePublishData = eventData;
            }
        }

        public void OnError(Exception error) { }

        public void OnCompleted() { }

        public void Dispose()
        {
            _listenerSubscription?.Dispose();
            _allListenersSubscription.Dispose();
        }

        private static bool _IsBeforePublish(string eventName, object? _, object? __) =>
            string.Equals(eventName, MessageDiagnosticListenerNames.BeforePublish, StringComparison.Ordinal);
    }
}
