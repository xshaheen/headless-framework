// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class MessagingIntentSplitTests : TestBase
{
    [Fact]
    public void should_be_stable_when_intent_type_storage_values()
    {
        // Persistence rows + on-wire serializations rely on these numeric values. Changing them is
        // a breaking change for any drained inbox/outbox row at-rest. Pin them explicitly.
        ((int)IntentType.Bus)
            .Should()
            .Be(0);
        ((int)IntentType.Queue).Should().Be(1);
    }

    [Fact]
    public void should_stamp_bus_intent_when_for_message_on_bus()
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
    public void should_stamp_queue_intent_when_for_message_on_queue()
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
    public void should_allow_same_topic_group_across_different_intents_when_consumer_registry()
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
    public async Task should_fail_when_bootstrap_queue_consumer_has_no_queue_transport()
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
    public async Task should_fail_when_bootstrap_bus_consumer_has_no_bus_transport()
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
    public async Task should_not_require_bus_transport_for_queue_only_consumer_when_bootstrap()
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
    public void should_register_only_queue_publishers_for_queue_only_transport_when_add_headless_messaging()
    {
        var services = new ServiceCollection();

        services.AddHeadlessMessaging(setup => setup.RegisterExtension(new QueueOnlyMessagingExtension()));

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IQueue));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IOutboxQueue));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IBus));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IOutboxBus));
    }

    [Fact]
    public void should_register_only_bus_publishers_for_bus_only_transport_when_add_headless_messaging()
    {
        var services = new ServiceCollection();

        services.AddHeadlessMessaging(setup => setup.RegisterExtension(new BusOnlyMessagingExtension()));

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IBus));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IOutboxBus));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IQueue));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IOutboxQueue));
    }

    [Fact]
    public async Task should_fail_when_bootstrap_bus_publisher_is_registered_without_bus_transport()
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
    public async Task should_fail_when_bootstrap_queue_publisher_is_registered_without_queue_transport()
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
    public async Task should_throw_publisher_sent_failed_when_bus_publish_transport_reports_failure()
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
    public async Task should_request_bus_intent_from_factory_and_trace_prepared_intent_when_bus_publish()
    {
        // given
        var publishRequestFactory = Substitute.For<IMessagePublishRequestFactory>();
        publishRequestFactory
            .Create(Arg.Any<TestMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<TimeSpan?>(), IntentType.Bus)
            .Returns(_CreatePreparedPublishMessage("events.prepared", IntentType.Bus));

        await using var transport = new CapturingBusTransport();
        var activities = new ConcurrentBag<Activity>();
        using var listener = _CreatePublishActivityListener(activities);
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
        transport.LastMessage!.Value.Name.Should().Be("events.prepared");
        // Scope the match to THIS test's destination: the listener is process-global, so other parallel tests
        // creating message.publish activities must not break the Single.
        var publishActivity = activities.Single(a =>
            string.Equals(a.OperationName, "message.publish", StringComparison.Ordinal)
            && Equals(a.GetTagItem("messaging.destination.name"), "events.prepared")
        );
        publishActivity.GetTagItem(MessagingTags.Intent).Should().Be("bus");
    }

    [Fact]
    public async Task should_throw_publisher_sent_failed_when_queue_enqueue_transport_reports_failure()
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
    public async Task should_request_queue_intent_from_factory_and_trace_prepared_intent_when_queue_enqueue()
    {
        // given
        var publishRequestFactory = Substitute.For<IMessagePublishRequestFactory>();
        publishRequestFactory
            .Create(Arg.Any<TestMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<TimeSpan?>(), IntentType.Queue)
            .Returns(_CreatePreparedPublishMessage("jobs.prepared", IntentType.Queue));

        await using var transport = new CapturingQueueTransport();
        var activities = new ConcurrentBag<Activity>();
        using var listener = _CreatePublishActivityListener(activities);
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
        transport.LastMessage!.Value.Name.Should().Be("jobs.prepared");
        // Scope the match to THIS test's destination: the listener is process-global, so other parallel tests
        // creating message.publish activities must not break the Single.
        var publishActivity = activities.Single(a =>
            string.Equals(a.OperationName, "message.publish", StringComparison.Ordinal)
            && Equals(a.GetTagItem("messaging.destination.name"), "jobs.prepared")
        );
        publishActivity.GetTagItem(MessagingTags.Intent).Should().Be("queue");
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
    )
    {
        return new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            optionsAccessor,
            new ConsumerRegistry(),
            new NullCurrentTenant()
        );
    }

    private static PreparedPublishMessage _CreatePreparedPublishMessage(string messageName, IntentType intentType)
    {
        return new()
        {
            MessageName = messageName,
            PublishAt = DateTimeOffset.UtcNow,
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
    }

    [Fact]
    public void should_treat_bus_and_queue_with_same_topic_group_as_distinct_when_descriptor_comparer()
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
    public void should_treat_same_topic_group_and_intent_as_equal_when_descriptor_comparer()
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
        public ValueTask ConsumeAsync(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestQueueConsumer : IConsume<TestMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpStorageInitializer : IStorageInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public string GetPublishedTableName()
        {
            return "published";
        }

        public string GetReceivedTableName()
        {
            return "received";
        }
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

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingQueueTransport : IQueueTransport
    {
        public BrokerAddress BrokerAddress { get; } = new("Test", "localhost");

        public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperateResult.Failed(new Exception("queue boom")));
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
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

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
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

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    // The listener is process-global and parallel tests' activities all land in `captured`, so the collection
    // must be thread-safe — a plain List.Add here races and can drop this test's own activity.
    private static ActivityListener _CreatePublishActivityListener(ConcurrentBag<Activity> captured)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                string.Equals(source.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal),
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = captured.Add,
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }
}
