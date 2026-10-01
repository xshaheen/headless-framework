// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tests.MultiTenancy;

namespace Tests;

// Hand-written modules in place of this assembly's generated one: each test host registers only the consumers its test
// needs, where the generated module would register every consumer declared in the assembly.

public sealed class OrderCreatedModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddBusConsumer<OrderCreatedConsumer, OrderCreatedEvent>(
            "tests.messaging-testing.order-created",
            everyInstance: false,
            TestDispatch.FromServices<OrderCreatedConsumer, OrderCreatedEvent>()
        );
}

public sealed class FailingModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddBusConsumer<FailingConsumer, OrderCreatedEvent>(
            "tests.messaging-testing.failing",
            everyInstance: false,
            TestDispatch.FromServices<FailingConsumer, OrderCreatedEvent>()
        );
}

public sealed class TestConsumerModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddBusConsumer<TestConsumer<OrderCreatedEvent>, OrderCreatedEvent>(
            "tests.messaging-testing.test-consumer",
            everyInstance: false,
            TestDispatch.FromServices<TestConsumer<OrderCreatedEvent>, OrderCreatedEvent>()
        );
}

public sealed class NotifyingModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddBusConsumer<NotifyingConsumer, OrderCreatedEvent>(
            "tests.messaging-testing.notifying",
            everyInstance: false,
            TestDispatch.FromServices<NotifyingConsumer, OrderCreatedEvent>()
        );
}

public sealed class LaneModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddBusConsumer<BusLaneConsumer, OrderCreatedEvent>(
            "tests.messaging-testing.bus-lane",
            everyInstance: false,
            TestDispatch.FromServices<BusLaneConsumer, OrderCreatedEvent>()
        );
        catalog.AddQueueConsumer<QueueLaneConsumer, OrderCreatedEvent>(
            "tests.messaging-testing.queue-lane",
            TestDispatch.FromServices<QueueLaneConsumer, OrderCreatedEvent>()
        );
    }
}

public sealed class DurableLaneModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddBusConsumer<BusLaneConsumer, OrderCreatedEvent>(
            "tests.messaging-testing.durable-bus-lane",
            everyInstance: false,
            TestDispatch.FromServices<BusLaneConsumer, OrderCreatedEvent>()
        );
        catalog.AddQueueConsumer<QueueLaneConsumer, OrderCreatedEvent>(
            "tests.messaging-testing.durable-queue-lane",
            TestDispatch.FromServices<QueueLaneConsumer, OrderCreatedEvent>()
        );
    }
}

public sealed class StandaloneOrderPlacedModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddBusConsumer<StandaloneOrderPlacedConsumer, StandaloneOrderPlaced>(
            "tests.messaging-testing.standalone-order-placed",
            everyInstance: false,
            TestDispatch.FromServices<StandaloneOrderPlacedConsumer, StandaloneOrderPlaced>()
        );
}

public sealed class CoordinatedOrderPlacedModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddBusConsumer<CoordinatedOrderPlacedConsumer, CoordinatedOrderPlaced>(
            "tests.messaging-testing.coordinated-order-placed",
            everyInstance: false,
            TestDispatch.FromServices<CoordinatedOrderPlacedConsumer, CoordinatedOrderPlaced>()
        );
}

public sealed class SharedHostModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddBusConsumer<AlphaConsumer, AlphaEvent>(
            "tests.messaging-testing.alpha",
            everyInstance: false,
            TestDispatch.FromServices<AlphaConsumer, AlphaEvent>()
        );
        catalog.AddBusConsumer<BetaConsumer, BetaEvent>(
            "tests.messaging-testing.beta",
            everyInstance: false,
            TestDispatch.FromServices<BetaConsumer, BetaEvent>()
        );
        catalog.AddBusConsumer<GatedConsumer, GammaEvent>(
            "tests.messaging-testing.gamma",
            everyInstance: false,
            TestDispatch.FromServices<GatedConsumer, GammaEvent>()
        );
        catalog.AddBusConsumer<DeltaGatedConsumer, DeltaEvent>(
            "tests.messaging-testing.delta",
            everyInstance: false,
            TestDispatch.FromServices<DeltaGatedConsumer, DeltaEvent>()
        );
    }
}

public sealed class TenantCaptureModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddBusConsumer<TenantCapturingConsumer, TenantOrderEvent>(
            "tests.tenant-propagation.capture",
            everyInstance: false,
            TestDispatch.FromServices<TenantCapturingConsumer, TenantOrderEvent>()
        );
}

public sealed class FlakyTenantModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddBusConsumer<FlakyTenantConsumer, TenantOrderEvent>(
            "tests.tenant-propagation.flaky",
            everyInstance: false,
            TestDispatch.FromServices<FlakyTenantConsumer, TenantOrderEvent>()
        );
}

public sealed class TenantRepublishModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddBusConsumer<ChainedRepublishConsumer, TenantOrderUpstream>(
            "tests.tenant-propagation.republish",
            everyInstance: false,
            TestDispatch.FromServices<ChainedRepublishConsumer, TenantOrderUpstream>()
        );
}

internal static class TestDispatch
{
    // Several tests assert on the consumer instance they registered as a singleton, which the generated dispatch,
    // always building a fresh instance, would bypass; a registered consumer is therefore resolved before one is built.
    public static MessageConsumerDispatch FromServices<TConsumer, TMessage>()
        where TConsumer : class, IConsume<TMessage>
        where TMessage : class =>
        static (services, context, cancellationToken) =>
            ActivatorUtilities
                .GetServiceOrCreateInstance<TConsumer>(services)
                .ConsumeAsync((ConsumeContext<TMessage>)context, cancellationToken);
}
