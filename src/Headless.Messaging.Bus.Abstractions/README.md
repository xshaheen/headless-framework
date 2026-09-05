# Headless.Messaging.Bus.Abstractions

Broadcast publisher contracts for Headless Messaging.

## Problem Solved

Gives application code a compile-time bus surface for publish/subscribe delivery where every matching subscriber group receives its own copy of a message.

## Key Features

- `IBus` is the only bus publisher; `PublishOptions.DeliveryMode` defaults to Durable; Auto and TransportDirect are explicit overrides.
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
        return bus.PublishAsync(message, cancellationToken);
    }
}
```

The short overload uses the registered message contract and captures durably by default, including outside a transaction. Pass `PublishOptions` before the cancellation token for metadata or delivery overrides. Durable acceptance waits for storage, not consumer completion; restart survival requires persistent storage. Inside a compatible coordination boundary the capture commits with application state, while an incompatible boundary is rejected. Explicit `Auto` captures in a compatible boundary and sends directly with no boundary. `TransportDirect` bypasses storage and coordination and cannot be combined with `Delay`.

## Configuration

None in this package. Runtime wiring is provided by `Headless.Messaging.Core` plus bus transport and storage providers.

## Dependencies

- `Headless.Messaging.Abstractions`

## Side Effects

None. This package registers no services.
