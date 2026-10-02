// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Registration;

// Hand-written equivalents of generated MessagingModule types. They stand in for separate module assemblies, so a test
// can put two modules that declare one identity or one Queue message into a single host.

public sealed record InvoiceIssued(string Number);

public sealed record OrderShipped(string OrderId);

public sealed record PriceChanged(string Sku);

public sealed record IssueInvoiceCommand(string OrderId);

/// <summary>Records what the test consumers and middleware did, in order.</summary>
public sealed class HostControlProbe
{
    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    public void Record(string call)
    {
        lock (_lock)
        {
            _calls.Add(call);
        }
    }
}

public sealed class BillingInvoiceProjection(HostControlProbe probe) : IConsume<InvoiceIssued>
{
    public ValueTask ConsumeAsync(ConsumeContext<InvoiceIssued> context, CancellationToken cancellationToken)
    {
        probe.Record($"billing {context.Message.Number}");
        return ValueTask.CompletedTask;
    }
}

public sealed class BillingPriceCache(HostControlProbe probe) : IConsume<PriceChanged>
{
    public ValueTask ConsumeAsync(ConsumeContext<PriceChanged> context, CancellationToken cancellationToken)
    {
        probe.Record($"price {context.Message.Sku}");
        return ValueTask.CompletedTask;
    }
}

public sealed class RivalInvoiceProjection : IConsume<InvoiceIssued>
{
    public ValueTask ConsumeAsync(ConsumeContext<InvoiceIssued> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

public sealed class RivalShipmentProjection : IConsume<OrderShipped>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderShipped> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

public sealed class OrdersShipmentProjection(HostControlProbe probe) : IConsume<OrderShipped>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderShipped> context, CancellationToken cancellationToken)
    {
        probe.Record($"orders {context.Message.OrderId}");
        return ValueTask.CompletedTask;
    }
}

public sealed class BillingIssueInvoice : IConsume<IssueInvoiceCommand>
{
    public ValueTask ConsumeAsync(ConsumeContext<IssueInvoiceCommand> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

public sealed class OrdersIssueInvoice : IConsume<IssueInvoiceCommand>
{
    public ValueTask ConsumeAsync(ConsumeContext<IssueInvoiceCommand> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

/// <summary>Consume middleware a test attaches to one consumer through <c>Tune</c>.</summary>
public sealed class AuditMiddleware(HostControlProbe probe) : IConsumeMiddleware<ConsumeContext>
{
    public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
    {
        probe.Record($"audit before {context.MessageType.Name}");
        await next();
        probe.Record($"audit after {context.MessageType.Name}");
    }
}

public sealed class BillingModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddBusConsumer<BillingInvoiceProjection, InvoiceIssued>(
            TestConsumers.InvoiceProjection,
            everyInstance: false,
            TestConsumers.Dispatch<BillingInvoiceProjection, InvoiceIssued>()
        );
}

/// <summary>An every-instance consumer outside the <c>orders</c> owner, for hosts that filter with <c>ConsumeOnly</c>.</summary>
public sealed class BillingPriceCacheModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddBusConsumer<BillingPriceCache, PriceChanged>(
            TestConsumers.PriceCache,
            everyInstance: true,
            TestConsumers.Dispatch<BillingPriceCache, PriceChanged>()
        );
}

/// <summary>Declares exactly what <see cref="BillingModule"/> declares, as a second assembly would on reuse.</summary>
public sealed class MirrorBillingModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) => BillingModule.Register(catalog);
}

public sealed class RivalInvoiceModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddBusConsumer<RivalInvoiceProjection, InvoiceIssued>(
            TestConsumers.InvoiceProjection,
            everyInstance: false,
            TestConsumers.Dispatch<RivalInvoiceProjection, InvoiceIssued>()
        );
}

public sealed class RivalShipmentModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddBusConsumer<RivalShipmentProjection, OrderShipped>(
            TestConsumers.InvoiceProjection,
            everyInstance: false,
            TestConsumers.Dispatch<RivalShipmentProjection, OrderShipped>()
        );
}

public sealed class OrdersModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddBusConsumer<OrdersShipmentProjection, OrderShipped>(
            TestConsumers.Shipment,
            everyInstance: false,
            TestConsumers.Dispatch<OrdersShipmentProjection, OrderShipped>()
        );
}

public sealed class BillingQueueModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddQueueConsumer<BillingIssueInvoice, IssueInvoiceCommand>(
            TestConsumers.BillingIssueInvoice,
            TestConsumers.Dispatch<BillingIssueInvoice, IssueInvoiceCommand>()
        );
}

public sealed class OrdersQueueModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        catalog.AddQueueConsumer<OrdersIssueInvoice, IssueInvoiceCommand>(
            TestConsumers.OrdersIssueInvoice,
            TestConsumers.Dispatch<OrdersIssueInvoice, IssueInvoiceCommand>()
        );
}

internal static class TestConsumers
{
    public const string InvoiceProjection = "billing.invoice-projection";
    public const string PriceCache = "billing.price-cache";
    public const string Shipment = "orders.shipment";
    public const string BillingIssueInvoice = "billing.issue-invoice";
    public const string OrdersIssueInvoice = "orders.issue-invoice";

    // Mirrors the generated dispatch: build the consumer from the delivery's scope, preferring its own registration, and
    // call the typed overload.
    public static MessageConsumerDispatch Dispatch<TConsumer, TMessage>()
        where TConsumer : class, IConsume<TMessage>
        where TMessage : class =>
        static (services, context, cancellationToken) =>
            Create<TConsumer>(services).ConsumeAsync((ConsumeContext<TMessage>)context, cancellationToken);

    // Mirrors the generated subscription hook: build the consumer the same way its dispatch does and call the hook.
    public static SubscriptionEstablishedDispatch SubscriptionHook<TConsumer>()
        where TConsumer : class, IOnSubscriptionEstablished =>
        static (services, context, cancellationToken) =>
            Create<TConsumer>(services).OnSubscriptionEstablishedAsync(context, cancellationToken);

    private static TConsumer Create<TConsumer>(IServiceProvider services)
        where TConsumer : class =>
        services.GetService<TConsumer>() ?? ActivatorUtilities.CreateInstance<TConsumer>(services);
}
