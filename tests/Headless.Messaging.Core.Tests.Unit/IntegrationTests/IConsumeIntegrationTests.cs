// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Runtime;
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
        services.ConfigureMessaging(messaging => messaging.Message<OrderPlaced>("orders.placed"));
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.AddConsumer<OrderPlacedConsumer>();
            messaging.Options.Version = "v1";
        });

        using var provider = services.BuildServiceProvider();

        // when - discovery
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();
        var candidates = selector.SelectCandidates();

        // then
        candidates.Should().ContainSingle();
        var descriptor = candidates[0];
        descriptor.MessageName.Should().Be("orders.placed");
        descriptor.GroupName.Should().Be(OrderPlacedConsumer.Identity);

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
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.Bus.ForMessage<OrderPlaced>(message =>
                message
                    .Contract("orders.placed")
                    .Consumer<OrderPlacedConsumer>(consumer =>
                        consumer.StableContract("tests.integration.orders-primary")
                    )
            );
            messaging.Options.DefaultGroupName = "default";
            messaging.Options.Version = "v1";
        });

        await using var provider = services.BuildServiceProvider();

        // Build consume context manually
        var message = new OrderPlaced("ORDER-123", 99.99m);
        var context = new ConsumeContext<OrderPlaced>
        {
            Lane = MessageLane.Bus,
            Message = message,
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            MessageName = "orders.placed",
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
        services.ConfigureMessaging(messaging => messaging.Message<OrderPlaced>("orders.placed"));
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.AddConsumer<OrderPlacedConsumer>();
            messaging.AddConsumer<OrderAnalyticsConsumer>();
            messaging.Options.Version = "v1";
        });

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        candidates.Should().HaveCount(2);

        var orderService = candidates.First(c =>
            string.Equals(c.GroupName, OrderPlacedConsumer.Identity, StringComparison.Ordinal)
        );
        var analyticsService = candidates.First(c =>
            string.Equals(c.GroupName, OrderAnalyticsConsumer.Identity, StringComparison.Ordinal)
        );

        orderService.ImplTypeInfo.Should().Be(typeof(OrderPlacedConsumer).GetTypeInfo());
        analyticsService.ImplTypeInfo.Should().Be(typeof(OrderAnalyticsConsumer).GetTypeInfo());
    }

    [Fact]
    public void should_use_topic_mapping_in_discovery()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(messaging =>
        {
            messaging.Bus.ForMessage<OrderPlaced>(message => message.Contract("orders.placed"));
            messaging.Bus.ForConsumersFromAssembly(
                typeof(IConsumeIntegrationTests).Assembly,
                StableConsumerTestContracts.ConfigureKnownScannedConsumer
            );
            messaging.Options.Version = "v1";
        });

        using var provider = services.BuildServiceProvider();
        var selector = provider.GetRequiredService<IConsumerServiceSelector>();

        // when
        var candidates = selector.SelectCandidates();

        // then
        var orderCandidates = candidates.Where(c =>
            c.ImplTypeInfo == typeof(OrderPlacedConsumer).GetTypeInfo()
            || c.ImplTypeInfo == typeof(OrderAnalyticsConsumer).GetTypeInfo()
        );

        orderCandidates.Should().AllSatisfy(c => c.MessageName.Should().Be("orders.placed"));
    }

    [Fact]
    public void should_handle_multi_message_consumer_registration()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(messaging =>
        {
            messaging.Bus.ForConsumersFromAssembly(
                typeof(IConsumeIntegrationTests).Assembly,
                StableConsumerTestContracts.ConfigureKnownScannedConsumer
            );
            messaging.Options.Version = "v1";
        });

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetDrainedConsumerRegistry();
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
        candidates.Should().Contain(c => c.MessageName == nameof(OrderPlaced));
        candidates.Should().Contain(c => c.MessageName == nameof(OrderCancelled));
    }

    [Fact]
    public async Task should_resolve_consumers_from_di_container()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(setup =>
            setup.Bus.ForMessage<OrderPlaced>(message =>
                message
                    .Contract("orders.placed")
                    .Consumer<OrderPlacedConsumer>(consumer =>
                        consumer.StableContract("tests.integration.orders-primary")
                    )
            )
        );

        await using var provider = services.BuildServiceProvider();

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
        services.AddHeadlessMessaging(setup =>
        {
            setup.Bus.ForMessage<OrderPlaced>(message =>
                message
                    .Contract("orders.placed")
                    .Consumer<OrderPlacedConsumer>(consumer =>
                        consumer.StableContract("tests.integration.orders-primary")
                    )
            );
            setup.Bus.ForMessage<OrderCancelled>(message =>
                message
                    .Contract("orders.cancelled")
                    .Consumer<OrderCancelledConsumer>(consumer =>
                        consumer.StableContract("tests.integration.orders-cancelled")
                    )
            );
        });

        await using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IMessageDispatcher>();

        var orderPlaced = new OrderPlaced("ORDER-1", 50m);
        var orderCancelled = new OrderCancelled("ORDER-2", "Customer request");

        var placedContext = new ConsumeContext<OrderPlaced>
        {
            Lane = MessageLane.Bus,
            Message = orderPlaced,
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            MessageName = "orders.placed",
        };

        var cancelledContext = new ConsumeContext<OrderCancelled>
        {
            Lane = MessageLane.Bus,
            Message = orderCancelled,
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = null,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            MessageName = "orders.cancelled",
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
[BusConsumer(Identity)]
public sealed class OrderPlacedConsumer : IConsume<OrderPlaced>
{
    public const string Identity = "tests.integration.orders-primary";

    public static OrderPlaced? LastProcessed { get; private set; }

    public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken)
    {
        LastProcessed = context.Message;
        return ValueTask.CompletedTask;
    }
}

[BusConsumer(Identity)]
public sealed class OrderAnalyticsConsumer : IConsume<OrderPlaced>
{
    public const string Identity = "tests.integration.orders-analytics";

    public static OrderPlaced? LastProcessed { get; private set; }

    public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken)
    {
        LastProcessed = context.Message;
        return ValueTask.CompletedTask;
    }
}

[BusConsumer("tests.integration.orders-cancelled")]
public sealed class OrderCancelledConsumer : IConsume<OrderCancelled>
{
    public static OrderCancelled? LastProcessed { get; private set; }

    public ValueTask ConsumeAsync(ConsumeContext<OrderCancelled> context, CancellationToken cancellationToken)
    {
        LastProcessed = context.Message;
        return ValueTask.CompletedTask;
    }
}

public sealed class MultiEventConsumer : IConsume<OrderPlaced>, IConsume<OrderCancelled>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask ConsumeAsync(ConsumeContext<OrderCancelled> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
