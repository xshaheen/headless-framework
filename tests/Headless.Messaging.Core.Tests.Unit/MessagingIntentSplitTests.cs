// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class MessagingIntentSplitTests : TestBase
{
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
            .WithMessage("*no IQueueTransport is available*");
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
}
