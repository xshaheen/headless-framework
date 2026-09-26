// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application orders, events, and variables the core guide's examples assume.

global using static CoreAmbient;
using Headless.Domain;
using Headless.Primitives;

public sealed class Order
{
    public Guid Id { get; init; }

    public required UserId UserId { get; init; }

    public string? TenantId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public required Money Total { get; init; }
}

public sealed record CreateOrderRequest(decimal Amount, string Currency);

public sealed record OrderCreatedEvent(Guid OrderId);

public sealed class OrderCreatedHandler : IDomainEventHandler<OrderCreatedEvent>
{
    public ValueTask HandleAsync(
        EventContext<OrderCreatedEvent> context,
        CancellationToken cancellationToken = default
    ) => ValueTask.CompletedTask;
}

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken);
}

#pragma warning disable IDE1006 // Ambient members mirror the locals and fields the examples use.
public static class CoreAmbient
{
    public static ILogger logger => null!;

    public static Guid orderId => default;

    public static IOrderRepository _repository => null!;
}
