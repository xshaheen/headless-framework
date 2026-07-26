# Headless.Messaging.Bus.Abstractions

Broadcast publisher contracts for Headless Messaging.

## Problem Solved

Gives application code a compile-time bus surface for publish/subscribe delivery where every matching subscriber group receives its own copy of a message.

## Key Features

- `IBus` is the only bus publisher; `PublishOptions.DeliveryMode` selects Auto, Durable, or TransportDirect.
- Durable delivery persists messages first, then drains them through the configured bus transport.
- `PublishOptions.Delay` schedules durable bus delivery.
- Every bus publish carries `MessageLane.Bus` through storage, tracing, dashboard projections, and consume context.

## Installation

```bash
dotnet add package Headless.Messaging.Bus.Abstractions
```

## Quick Start

```csharp
public sealed class OrderEvents(IBus bus)
{
    public Task PublishAsync(OrderPlaced message, CancellationToken cancellationToken)
    {
        return bus.PublishAsync(
            message,
            new PublishOptions { MessageName = "orders.placed", DeliveryMode = DeliveryMode.Durable },
            cancellationToken
        );
    }
}
```

Use `DeliveryMode.Durable` when the publish must survive process crashes, `TransportDirect` to bypass storage and any ambient coordination boundary, or the default `Auto` to capture in a compatible boundary and send directly with no boundary. `Auto` rejects an active incompatible boundary, and `TransportDirect` cannot be combined with `Delay`.

## Configuration

None in this package. Runtime wiring is provided by `Headless.Messaging.Core` plus bus transport and storage providers.

## Dependencies

- `Headless.Messaging.Abstractions`

## Side Effects

None. This package registers no services.
