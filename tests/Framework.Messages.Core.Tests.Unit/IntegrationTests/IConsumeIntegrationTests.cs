// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.IntegrationTests;

public class IConsumeIntegrationTests
{
    [Fact]
    public async Task should_register_and_discover_consumers_end_to_end()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessages(messaging =>
        {
            messaging.Consumer<OrderPlacedConsumer>().Topic("orders.placed").Group("order-service").Build();
        });

        services.Configure<MessagingOptions>(opt =>
        {
            opt.DefaultGroupName = "default";
            opt.Version = "v1";
        });

        var provider = services.BuildServiceProvider();

        // When - discovery
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();
        var candidates = selector.SelectCandidates();

        // Then
        candidates.Should().HaveCount(1);
        var descriptor = candidates[0];
        descriptor.TopicName.Should().Be("orders.placed");
        descriptor.GroupName.Should().Be("order-service.v1");

        // And - selection
        var best = selector.SelectBestCandidate("orders.placed", candidates);
        best.Should().NotBeNull();
        best!.ImplTypeInfo.Should().Be(typeof(OrderPlacedConsumer).GetTypeInfo());
    }

    [Fact]
    public async Task should_invoke_handler_through_dispatcher()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessages(messaging =>
        {
            messaging.Consumer<OrderPlacedConsumer>().Topic("orders.placed").Build();
        });

        services.Configure<MessagingOptions>(opt =>
        {
            opt.DefaultGroupName = "default";
            opt.Version = "v1";
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

        // When
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IMessageDispatcher>();
        await dispatcher.DispatchAsync(context, CancellationToken.None);

        // Then
        OrderPlacedConsumer.LastProcessed.Should().NotBeNull();
        OrderPlacedConsumer.LastProcessed!.OrderId.Should().Be("ORDER-123");
        OrderPlacedConsumer.LastProcessed.Amount.Should().Be(99.99m);
    }

    [Fact]
    public async Task should_support_multiple_consumers_for_different_groups()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessages(messaging =>
        {
            messaging.Consumer<OrderPlacedConsumer>().Topic("orders.placed").Group("order-service").Build();

            messaging.Consumer<OrderAnalyticsConsumer>().Topic("orders.placed").Group("analytics-service").Build();
        });

        services.Configure<MessagingOptions>(opt =>
        {
            opt.DefaultGroupName = "default";
            opt.Version = "v1";
        });

        var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // When
        var candidates = selector.SelectCandidates();

        // Then
        candidates.Should().HaveCount(2);

        var orderService = candidates.First(c => c.GroupName.Contains("order-service"));
        var analyticsService = candidates.First(c => c.GroupName.Contains("analytics-service"));

        orderService.ImplTypeInfo.Should().Be(typeof(OrderPlacedConsumer).GetTypeInfo());
        analyticsService.ImplTypeInfo.Should().Be(typeof(OrderAnalyticsConsumer).GetTypeInfo());
    }

    [Fact]
    public async Task should_use_topic_mapping_in_discovery()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessages(messaging =>
        {
            messaging.WithTopicMapping<OrderPlaced>("orders.placed");
            messaging.ScanConsumers(typeof(IConsumeIntegrationTests).Assembly);
        });

        services.Configure<MessagingOptions>(opt =>
        {
            opt.DefaultGroupName = "default";
            opt.Version = "v1";
        });

        var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // When
        var candidates = selector.SelectCandidates();

        // Then
        var orderCandidates = candidates.Where(c =>
            c.ImplTypeInfo == typeof(OrderPlacedConsumer).GetTypeInfo()
            || c.ImplTypeInfo == typeof(OrderAnalyticsConsumer).GetTypeInfo()
        );

        orderCandidates.Should().AllSatisfy(c => c.TopicName.Should().Be("orders.placed"));
    }

    [Fact]
    public async Task should_handle_multi_message_consumer_registration()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessages(messaging =>
        {
            messaging.ScanConsumers(typeof(IConsumeIntegrationTests).Assembly);
        });

        services.Configure<MessagingOptions>(opt =>
        {
            opt.DefaultGroupName = "default";
            opt.Version = "v1";
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ConsumerRegistry>();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // When
        var registryEntries = registry.GetAll();
        var candidates = selector.SelectCandidates();

        // Then - registry should have 2 entries for MultiEventConsumer
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
        // Given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMessages(messaging =>
        {
            messaging.Consumer<OrderPlacedConsumer>().Topic("orders.placed").Build();
        });

        var provider = services.BuildServiceProvider();

        // When
        using var scope = provider.CreateScope();
        var consumer = scope.ServiceProvider.GetService<IConsume<OrderPlaced>>();

        // Then
        consumer.Should().NotBeNull();
        consumer.Should().BeOfType<OrderPlacedConsumer>();
    }

    [Fact]
    public async Task should_dispatch_to_correct_handler_based_on_message_type()
    {
        // Given
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

        // When
        await dispatcher.DispatchAsync(placedContext, CancellationToken.None);
        await dispatcher.DispatchAsync(cancelledContext, CancellationToken.None);

        // Then
        OrderPlacedConsumer.LastProcessed.Should().NotBeNull();
        OrderPlacedConsumer.LastProcessed!.OrderId.Should().Be("ORDER-1");

        OrderCancelledConsumer.LastProcessed.Should().NotBeNull();
        OrderCancelledConsumer.LastProcessed!.OrderId.Should().Be("ORDER-2");
    }
}

// Test messages
public sealed record OrderPlaced(string OrderId, decimal Amount);

public sealed record OrderCancelled(string OrderId, string Reason);

// Test consumers
public sealed class OrderPlacedConsumer : IConsume<OrderPlaced>
{
    public static OrderPlaced? LastProcessed { get; private set; }

    public ValueTask Consume(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken = default)
    {
        LastProcessed = context.Message;
        return ValueTask.CompletedTask;
    }
}

public sealed class OrderAnalyticsConsumer : IConsume<OrderPlaced>
{
    public static OrderPlaced? LastProcessed { get; private set; }

    public ValueTask Consume(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken = default)
    {
        LastProcessed = context.Message;
        return ValueTask.CompletedTask;
    }
}

public sealed class OrderCancelledConsumer : IConsume<OrderCancelled>
{
    public static OrderCancelled? LastProcessed { get; private set; }

    public ValueTask Consume(ConsumeContext<OrderCancelled> context, CancellationToken cancellationToken = default)
    {
        LastProcessed = context.Message;
        return ValueTask.CompletedTask;
    }
}

public sealed class MultiEventConsumer : IConsume<OrderPlaced>, IConsume<OrderCancelled>
{
    public ValueTask Consume(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask Consume(ConsumeContext<OrderCancelled> context, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}
