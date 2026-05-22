// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class MessagingIntentSplitTests : TestBase
{
    [Fact(Skip = "Plan 003")]
    public void Plan003_OutboxBus_persists_bus_intent_on_row()
    {
        // Asserts that IOutboxBus.PublishAsync persists the IntentType.Bus value onto the outbox
        // row. Currently OutboxBus delegates to IOutboxPublisher, which has no IntentType
        // parameter. Plan 003 introduces IntentType-on-row persistence; this guard test lights up
        // when the parameter is threaded through.
        Assert.Fail("Plan 003 — IntentType-on-row persistence not yet implemented.");
    }

    [Fact(Skip = "Plan 003")]
    public void Plan003_OutboxQueue_persists_queue_intent_on_row()
    {
        // Twin for IOutboxQueue: asserts IntentType.Queue is captured on the persisted row.
        Assert.Fail("Plan 003 — IntentType-on-row persistence not yet implemented.");
    }

    [Fact]
    public void intent_type_storage_values_should_be_stable()
    {
        // Persistence rows + on-wire serializations rely on these numeric values. Changing them is
        // a breaking change for any drained inbox/outbox row at-rest. Pin them explicitly.
        Assert.Equal(0, (int)IntentType.Bus);
        Assert.Equal(1, (int)IntentType.Queue);
    }

    [Fact]
    public void add_bus_consumer_should_stamp_bus_intent()
    {
        var services = new ServiceCollection();

        services.AddBusConsumer<TestBusConsumer, TestMessage>("events.orders");

        var metadata = services.BuildServiceProvider().GetRequiredService<ConsumerMetadata>();

        metadata.IntentType.Should().Be(IntentType.Bus);
        metadata.Topic.Should().Be("events.orders");
    }

    [Fact]
    public void add_queue_consumer_should_stamp_queue_intent()
    {
        var services = new ServiceCollection();

        services.AddQueueConsumer<TestQueueConsumer, TestMessage>("jobs.orders");

        var metadata = services.BuildServiceProvider().GetRequiredService<ConsumerMetadata>();

        metadata.IntentType.Should().Be(IntentType.Queue);
        metadata.Topic.Should().Be("jobs.orders");
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
        var bootstrapper = new Bootstrapper(
            [],
            new NoOpStorageInitializer(),
            provider,
            Options.Create(new MessagingOptions()),
            NullLogger<IBootstrapper>.Instance
        );

        var act = () => bootstrapper.BootstrapAsync(AbortToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*IQueueTransport*available*");
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
        // message naming AddBusConsumer<...>.
        services.AddSingleton(new MessagingMarkerService("Messaging"));
        services.AddSingleton(new MessageQueueMarkerService("TestTransport"));
        services.AddSingleton(new MessageStorageMarkerService("TestStorage"));
        services.AddSingleton<IConsumerRegistry>(registry);
        services.AddSingleton(registry);

        await using var provider = services.BuildServiceProvider();
        var bootstrapper = new Bootstrapper(
            [],
            new NoOpStorageInitializer(),
            provider,
            Options.Create(new MessagingOptions()),
            NullLogger<IBootstrapper>.Instance
        );

        var act = () => bootstrapper.BootstrapAsync(AbortToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*IBusTransport*available*");
    }

    [Fact]
    public async Task bus_publish_should_throw_publisher_sent_failed_when_transport_reports_failure()
    {
        // given
        var transport = new FailingBusTransport();
        var bus = _CreateBus(transport);

        // when
        var act = () => bus.PublishAsync(new TestMessage(), cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<PublisherSentFailedException>();
    }

    [Fact]
    public async Task queue_enqueue_should_throw_publisher_sent_failed_when_transport_reports_failure()
    {
        // given
        var transport = new FailingQueueTransport();
        var queue = _CreateQueue(transport);

        // when
        var act = () => queue.EnqueueAsync(new TestMessage(), cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<PublisherSentFailedException>();
    }

    [Fact]
    public async Task outbox_bus_should_route_to_direct_publisher_when_no_delay_is_set()
    {
        // given
        var publisher = Substitute.For<IOutboxPublisher>();
        var scheduled = Substitute.For<IScheduledPublisher>();
        var outboxBus = new OutboxBus(publisher, scheduled);
        var options = new PublishOptions();

        // when
        await outboxBus.PublishAsync(new TestMessage(), options, AbortToken);

        // then
        await publisher
            .Received(1)
            .PublishAsync(Arg.Any<TestMessage>(), Arg.Any<PublishOptions>(), Arg.Any<CancellationToken>());
        await scheduled
            .DidNotReceive()
            .PublishDelayAsync(
                Arg.Any<TimeSpan>(),
                Arg.Any<TestMessage>(),
                Arg.Any<PublishOptions>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task outbox_bus_should_route_to_scheduled_publisher_when_delay_is_set()
    {
        // given
        var publisher = Substitute.For<IOutboxPublisher>();
        var scheduled = Substitute.For<IScheduledPublisher>();
        var outboxBus = new OutboxBus(publisher, scheduled);
        var delay = TimeSpan.FromMinutes(5);
        var options = new PublishOptions { Delay = delay };

        // when
        await outboxBus.PublishAsync(new TestMessage(), options, AbortToken);

        // then
        await scheduled
            .Received(1)
            .PublishDelayAsync(
                delay,
                Arg.Any<TestMessage>(),
                Arg.Any<PublishOptions>(),
                Arg.Any<CancellationToken>()
            );
        await publisher
            .DidNotReceive()
            .PublishAsync(Arg.Any<TestMessage>(), Arg.Any<PublishOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task outbox_queue_should_route_to_direct_publisher_when_no_delay_is_set()
    {
        // given
        var publisher = Substitute.For<IOutboxPublisher>();
        var scheduled = Substitute.For<IScheduledPublisher>();
        var outboxQueue = new OutboxQueue(publisher, scheduled);

        // when
        await outboxQueue.EnqueueAsync(new TestMessage(), options: null, AbortToken);

        // then
        await publisher
            .Received(1)
            .PublishAsync(Arg.Any<TestMessage>(), Arg.Any<PublishOptions>(), Arg.Any<CancellationToken>());
        await scheduled
            .DidNotReceive()
            .PublishDelayAsync(
                Arg.Any<TimeSpan>(),
                Arg.Any<TestMessage>(),
                Arg.Any<PublishOptions>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task outbox_queue_should_route_to_scheduled_publisher_when_delay_is_set()
    {
        // given
        var publisher = Substitute.For<IOutboxPublisher>();
        var scheduled = Substitute.For<IScheduledPublisher>();
        var outboxQueue = new OutboxQueue(publisher, scheduled);
        var delay = TimeSpan.FromMinutes(5);
        var options = new EnqueueOptions { Delay = delay };

        // when
        await outboxQueue.EnqueueAsync(new TestMessage(), options, AbortToken);

        // then
        await scheduled
            .Received(1)
            .PublishDelayAsync(
                delay,
                Arg.Any<TestMessage>(),
                Arg.Any<PublishOptions>(),
                Arg.Any<CancellationToken>()
            );
        await publisher
            .DidNotReceive()
            .PublishAsync(Arg.Any<TestMessage>(), Arg.Any<PublishOptions>(), Arg.Any<CancellationToken>());
    }

    private static IBus _CreateBus(IBusTransport transport)
    {
        var optionsAccessor = Options.Create(new MessagingOptions());
        var serializer = new JsonUtf8Serializer(optionsAccessor);
        var publishRequestFactory = new MessagePublishRequestFactory(
            new SnowflakeIdLongIdGenerator(),
            TimeProvider.System,
            optionsAccessor,
            new NullCurrentTenant()
        );
        var pipeline = new PublishMiddlewarePipeline(new ServiceCollection().BuildServiceProvider());

        return new Bus(serializer, transport, publishRequestFactory, pipeline, TimeProvider.System);
    }

    private static IQueue _CreateQueue(IQueueTransport transport)
    {
        var optionsAccessor = Options.Create(new MessagingOptions());
        var serializer = new JsonUtf8Serializer(optionsAccessor);
        var publishRequestFactory = new MessagePublishRequestFactory(
            new SnowflakeIdLongIdGenerator(),
            TimeProvider.System,
            optionsAccessor,
            new NullCurrentTenant()
        );
        var pipeline = new PublishMiddlewarePipeline(new ServiceCollection().BuildServiceProvider());

        return new Queue(serializer, transport, publishRequestFactory, pipeline, TimeProvider.System);
    }

    [Fact]
    public void descriptor_comparer_should_treat_bus_and_queue_with_same_topic_group_as_distinct()
    {
        var comparer = new ConsumerExecutorDescriptorComparer(
            NullLogger<ConsumerExecutorDescriptorComparer>.Instance
        );
        var implTypeInfo = typeof(TestBusConsumer).GetTypeInfo();
        var methodInfo = typeof(TestBusConsumer).GetMethod(
            nameof(TestBusConsumer.Consume),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            [typeof(ConsumeContext<TestMessage>), typeof(CancellationToken)]
        )!;

        var busDescriptor = new ConsumerExecutorDescriptor
        {
            MethodInfo = methodInfo,
            ImplTypeInfo = implTypeInfo,
            TopicName = "orders.created",
            GroupName = "workers",
            IntentType = IntentType.Bus,
        };
        var queueDescriptor = new ConsumerExecutorDescriptor
        {
            MethodInfo = methodInfo,
            ImplTypeInfo = implTypeInfo,
            TopicName = "orders.created",
            GroupName = "workers",
            IntentType = IntentType.Queue,
        };

        comparer.Equals(busDescriptor, queueDescriptor).Should().BeFalse();
        comparer.GetHashCode(busDescriptor).Should().NotBe(comparer.GetHashCode(queueDescriptor));
    }

    [Fact]
    public void descriptor_comparer_should_treat_same_topic_group_and_intent_as_equal()
    {
        var comparer = new ConsumerExecutorDescriptorComparer(
            NullLogger<ConsumerExecutorDescriptorComparer>.Instance
        );
        var implTypeInfo = typeof(TestBusConsumer).GetTypeInfo();
        var methodInfo = typeof(TestBusConsumer).GetMethod(
            nameof(TestBusConsumer.Consume),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            [typeof(ConsumeContext<TestMessage>), typeof(CancellationToken)]
        )!;

        var first = new ConsumerExecutorDescriptor
        {
            MethodInfo = methodInfo,
            ImplTypeInfo = implTypeInfo,
            TopicName = "orders.created",
            GroupName = "workers",
            IntentType = IntentType.Bus,
        };
        var second = new ConsumerExecutorDescriptor
        {
            MethodInfo = methodInfo,
            ImplTypeInfo = implTypeInfo,
            TopicName = "orders.created",
            GroupName = "workers",
            IntentType = IntentType.Bus,
        };

        comparer.Equals(first, second).Should().BeTrue();
        comparer.GetHashCode(first).Should().Be(comparer.GetHashCode(second));
    }

    private sealed record TestMessage;

    private sealed class TestBusConsumer : IConsume<TestMessage>
    {
        public ValueTask Consume(ConsumeContext<TestMessage> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class TestQueueConsumer : IConsume<TestMessage>
    {
        public ValueTask Consume(ConsumeContext<TestMessage> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class NoOpStorageInitializer : IStorageInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public string GetPublishedTableName() => "published";

        public string GetReceivedTableName() => "received";
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
}
