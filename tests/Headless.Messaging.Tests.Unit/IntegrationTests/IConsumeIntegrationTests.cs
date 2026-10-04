// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
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
        descriptor.SubscriptionName.Should().Be(OrderPlacedConsumer.Identity);

        // And - selection
        var best = selector.SelectBestCandidate("orders.placed", candidates);
        best.Should().NotBeNull();
        best.ConsumerType.Should().Be<OrderPlacedConsumer>();
    }

    [Fact]
    public void should_support_multiple_consumers_for_different_identities()
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
            string.Equals(c.SubscriptionName, OrderPlacedConsumer.Identity, StringComparison.Ordinal)
        );
        var analyticsService = candidates.First(c =>
            string.Equals(c.SubscriptionName, OrderAnalyticsConsumer.Identity, StringComparison.Ordinal)
        );

        orderService.ConsumerType.Should().Be<OrderPlacedConsumer>();
        analyticsService.ConsumerType.Should().Be<OrderAnalyticsConsumer>();
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
