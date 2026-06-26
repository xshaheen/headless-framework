# Headless.Messaging.Bus.Abstractions

Broadcast publisher contracts for Headless Messaging.

## Problem Solved

Gives application code a compile-time bus surface for publish/subscribe delivery where every matching subscriber group receives its own copy of a message.

## Key Features

- `IBus` publishes directly to the configured bus transport.
- `IOutboxBus` persists messages first, then drains them through the configured bus transport.
- `PublishOptions.Delay` schedules delayed outbox bus delivery.
- Every bus publish carries `IntentType.Bus` through storage, tracing, dashboard projections, and consume context.

## Installation

```bash
dotnet add package Headless.Messaging.Bus.Abstractions
```

## Quick Start

```csharp
public sealed class OrderEvents(IOutboxBus bus)
{
    public Task PublishAsync(OrderPlaced message, CancellationToken cancellationToken)
    {
        return bus.PublishAsync(message, new PublishOptions { MessageName = "orders.placed" }, cancellationToken);
    }
}
```

Use `IBus` only when transport-direct, fire-and-forget delivery is acceptable. Use `IOutboxBus` when the publish must survive process crashes or coordinate with an application transaction.

## Configuration

None in this package. Runtime wiring is provided by `Headless.Messaging.Core` plus a provider that registers `IBusTransport`; without that provider, `IBus` and `IOutboxBus` are not registered.

## Dependencies

- `Headless.Messaging.Abstractions`

## Side Effects

None. This package registers no services.
