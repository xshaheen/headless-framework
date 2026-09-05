# Headless.Messaging.Bus.Abstractions

Broadcast publisher contracts for Headless Messaging.

## Problem Solved

Gives application code a compile-time bus surface for publish/subscribe delivery where every matching subscriber group receives its own copy of a message.

## Key Features

- `IBus` is the only bus publisher; `PublishOptions.DeliveryMode` defaults to Durable; Auto and TransportDirect are explicit overrides.
- Durable delivery persists messages first, then drains them through the configured bus transport.
- `PublishOptions.Delay` schedules durable bus delivery.
- `PublishOptionsBuilder` and the `BusExtensions.PublishAsync` callback author canonical options snapshots without a Core dependency.
- Every bus publish carries `MessageLane.Bus` through storage, tracing, dashboard projections, and consume context.

## Installation

```bash
dotnet add package Headless.Messaging.Bus.Abstractions
```

## Quick Start

```csharp
using Headless.Messaging;

public sealed class OrderEvents(IBus bus)
{
    public Task PublishAsync(OrderPlaced message, CancellationToken cancellationToken)
    {
        return bus.PublishAsync(message, cancellationToken);
    }

    public Task PublishWithMetadataAsync(OrderPlaced message, string correlationId, CancellationToken cancellationToken)
    {
        return bus.PublishAsync(message, options => options
            .WithHeader("source", "checkout")
            .WithCorrelationId(correlationId), cancellationToken);
    }
}

public sealed record OrderPlaced(Guid OrderId);
```

The short overload uses the registered message contract and captures durably by default, including outside a transaction. Pass `PublishOptions` before the cancellation token for metadata or delivery overrides. Durable acceptance waits for storage, not consumer completion; restart survival requires persistent storage. Inside a compatible coordination boundary the capture commits with application state, while an incompatible boundary is rejected. Explicit `Auto` captures in a compatible boundary and sends directly with no boundary. `TransportDirect` bypasses storage and coordination and cannot be combined with `Delay`.

Omit an unused cancellation token, or pass a literal default token as `cancellationToken: default`. A bare positional `default` is ambiguous between the token and options overloads; typed token variables and explicit options remain valid.

Import `Headless.Messaging` for `PublishOptionsBuilder` and the callback extension. Its operations are `WithHeader`, `WithHeaders`, `WithCorrelationId`, `WithCausationId`, `WithMessageId`, `WithTenantId`, `WithDelay`, and `Build`. Use a callback for a single authoring scope or construct a builder directly and call `Build()` for a reusable options template. Delivery-mode and other advanced overrides remain available through `PublishOptions` or `builder.Build() with { ... }`.

Each callback runs synchronously exactly once on a fresh builder; async-void callbacks are unsupported. A null receiver or `configure` throws before user code, and a throwing callback submits nothing. The adapter forwards the original token and returns the original task. `options: null` and positional `null` keep the existing options path; `configure: null!` selects the callback guard.

Builders support sequential reuse, not concurrent mutation. Header input is copied immediately and again on each `Build()`: ordinal keys, last-write-wins merges, distinct casing, and null values are preserved. No header call leaves `Headers` null; an empty supplied collection creates an empty dictionary. Each result owns mutable headers independently. Nullable metadata and delay setters accept null to clear an explicit value. `Build()` retains `DeliveryMode.Durable` and does not validate or accept delivery; the publisher still validates headers, tenancy, and positive delays (zero is invalid).

## Configuration

None in this package. Runtime wiring is provided by `Headless.Messaging.Core` plus bus transport and storage providers.

## Dependencies

- `Headless.Messaging.Abstractions`

## Side Effects

None. This package registers no services.
