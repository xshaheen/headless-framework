// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.IntegrationTests;

public sealed class IConsumeIntegrationTests
{
    [Fact]
    public void should_register_and_discover_consumers_end_to_end()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "default";
            messaging.Version = "v1";
            messaging.Consumer<OrderPlacedConsumer>().Topic("orders.placed").Group("order-service").Build();
        });

        var provider = services.BuildServiceProvider();

        // when - discovery
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();
        var candidates = selector.SelectCandidates();

        // then
        candidates.Should().HaveCount(1);
        var descriptor = candidates[0];
        descriptor.TopicName.Should().Be("orders.placed");
        descriptor.GroupName.Should().Be("order-service.v1");

        // And - selection
        var best = selector.SelectBestCandidate("orders.placed", candidates);
        best.Should().NotBeNull();
        best.ImplTypeInfo.Should().Be(typeof(OrderPlacedConsumer).GetTypeInfo());
    }

    [Fact]
    public async Task should_invoke_handler_through_dispatcher()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "default";
            messaging.Version = "v1";
            messaging.Consumer<OrderPlacedConsumer>().Topic("orders.placed").Build();
        });

        var provider = services.BuildServiceProvider();

        // Build consume context manually
        var message = new OrderPlaced("ORDER-123", 99.99m);
        var context = new ConsumeContext<OrderPlaced>
        {
            Message = message,
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "orders.placed",
        };

        // when
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IMessageDispatcher>();
        await dispatcher.DispatchAsync(context, CancellationToken.None);

        // then
        OrderPlacedConsumer.LastProcessed.Should().NotBeNull();
        OrderPlacedConsumer.LastProcessed.OrderId.Should().Be("ORDER-123");
        OrderPlacedConsumer.LastProcessed.Amount.Should().Be(99.99m);
    }

    [Fact]
    public void should_support_multiple_consumers_for_different_groups()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "default";
            messaging.Version = "v1";
            messaging.Consumer<OrderPlacedConsumer>().Topic("orders.placed").Group("order-service").Build();
            messaging.Consumer<OrderAnalyticsConsumer>().Topic("orders.placed").Group("analytics-service").Build();
        });

        var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        candidates.Should().HaveCount(2);

        var orderService = candidates.First(c => c.GroupName.Contains("order-service"));
        var analyticsService = candidates.First(c => c.GroupName.Contains("analytics-service"));

        orderService.ImplTypeInfo.Should().Be(typeof(OrderPlacedConsumer).GetTypeInfo());
        analyticsService.ImplTypeInfo.Should().Be(typeof(OrderAnalyticsConsumer).GetTypeInfo());
    }

    [Fact]
    public void should_use_topic_mapping_in_discovery()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "default";
            messaging.Version = "v1";
            messaging.WithTopicMapping<OrderPlaced>("orders.placed");
            messaging.ScanConsumers(typeof(IConsumeIntegrationTests).Assembly);
        });

        var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        var orderCandidates = candidates.Where(c =>
            c.ImplTypeInfo == typeof(OrderPlacedConsumer).GetTypeInfo()
            || c.ImplTypeInfo == typeof(OrderAnalyticsConsumer).GetTypeInfo()
        );

        orderCandidates.Should().AllSatisfy(c => c.TopicName.Should().Be("orders.placed"));
    }

    [Fact]
    public void should_handle_multi_message_consumer_registration()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessages(messaging =>
        {
            messaging.DefaultGroupName = "default";
            messaging.Version = "v1";
            messaging.ScanConsumers(typeof(IConsumeIntegrationTests).Assembly);
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConsumerRegistry>();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var registryEntries = registry.GetAll();
        var candidates = selector.SelectCandidates();

        // then - registry should have 2 entries for MultiEventConsumer
        var multiInRegistry = registryEntries.Where(c => c.ConsumerType == typeof(MultiEventConsumer)).ToList();
        multiInRegistry.Should().HaveCount(2);
        multiInRegistry.Should().Contain(c => c.MessageType == typeof(OrderPlaced));
        multiInRegistry.Should().Contain(c => c.MessageType == typeof(OrderCancelled));

        // And - selector should convert them to descriptors
        candidates.Should().Contain(c => c.TopicName == nameof(OrderPlaced));
        candidates.Should().Contain(c => c.TopicName == nameof(OrderCancelled));
    }

    [Fact]
    public async Task should_resolve_consumers_from_di_container()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessages(messaging =>
        {
            messaging.Consumer<OrderPlacedConsumer>().Topic("orders.placed").Build();
        });

        var provider = services.BuildServiceProvider();

        // when
        await using var scope = provider.CreateAsyncScope();
        var consumer = scope.ServiceProvider.GetService<IConsume<OrderPlaced>>();

        // then
        consumer.Should().NotBeNull();
        consumer.Should().BeOfType<OrderPlacedConsumer>();
    }

    [Fact]
    public async Task should_dispatch_to_correct_handler_based_on_message_type()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessages(messaging =>
        {
            messaging.Consumer<OrderPlacedConsumer>().Topic("orders.placed").Build();

            messaging.Consumer<OrderCancelledConsumer>().Topic("orders.cancelled").Build();
        });

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IMessageDispatcher>();

        var orderPlaced = new OrderPlaced("ORDER-1", 50m);
        var orderCancelled = new OrderCancelled("ORDER-2", "Customer request");

        var placedContext = new ConsumeContext<OrderPlaced>
        {
            Message = orderPlaced,
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "orders.placed",
        };

        var cancelledContext = new ConsumeContext<OrderCancelled>
        {
            Message = orderCancelled,
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            Topic = "orders.cancelled",
        };

        // when
        await dispatcher.DispatchAsync(placedContext, CancellationToken.None);
        await dispatcher.DispatchAsync(cancelledContext, CancellationToken.None);

        // then
        OrderPlacedConsumer.LastProcessed.Should().NotBeNull();
        OrderPlacedConsumer.LastProcessed.OrderId.Should().Be("ORDER-1");

        OrderCancelledConsumer.LastProcessed.Should().NotBeNull();
        OrderCancelledConsumer.LastProcessed.OrderId.Should().Be("ORDER-2");
    }
}

// Test messages
public sealed record OrderPlaced(string OrderId, decimal Amount);

public sealed record OrderCancelled(string OrderId, string Reason);

// Test consumers
public sealed class OrderPlacedConsumer : IConsume<OrderPlaced>
{
    public static OrderPlaced? LastProcessed { get; private set; }

    public ValueTask Consume(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken)
    {
        LastProcessed = context.Message;
        return ValueTask.CompletedTask;
    }
}

public sealed class OrderAnalyticsConsumer : IConsume<OrderPlaced>
{
    public static OrderPlaced? LastProcessed { get; private set; }

    public ValueTask Consume(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken)
    {
        LastProcessed = context.Message;
        return ValueTask.CompletedTask;
    }
}

public sealed class OrderCancelledConsumer : IConsume<OrderCancelled>
{
    public static OrderCancelled? LastProcessed { get; private set; }

    public ValueTask Consume(ConsumeContext<OrderCancelled> context, CancellationToken cancellationToken)
    {
        LastProcessed = context.Message;
        return ValueTask.CompletedTask;
    }
}

public sealed class MultiEventConsumer : IConsume<OrderPlaced>, IConsume<OrderCancelled>
{
    public ValueTask Consume(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask Consume(ConsumeContext<OrderCancelled> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
