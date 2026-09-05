# Headless.Messaging.Queue.Abstractions

Point-to-point publisher contracts for Headless Messaging.

## Problem Solved

Gives application code a compile-time queue surface for work-queue delivery where exactly one competing worker handles each message.

## Key Features

- `IQueue` is the only queue publisher; `QueueOptions.DeliveryMode` defaults to Durable; Auto and TransportDirect are explicit overrides.
- Durable delivery persists messages first, then drains them through the configured queue transport.
- `QueueOptions.Delay` schedules durable queue delivery.
- `QueueOptionsBuilder` and the `QueueExtensions.EnqueueAsync` callback author canonical options snapshots without a Core dependency.
- Every queue enqueue carries `MessageLane.Queue` through storage, tracing, dashboard projections, and consume context.

## Installation

```bash
dotnet add package Headless.Messaging.Queue.Abstractions
```

## Quick Start

```csharp
using Headless.Messaging;

public sealed class ImportJobs(IQueue queue)
{
    public Task EnqueueAsync(ImportRequested message, CancellationToken cancellationToken)
    {
        return queue.EnqueueAsync(message, cancellationToken);
    }

    public Task EnqueueWithMetadataAsync(ImportRequested message, string correlationId, CancellationToken cancellationToken)
    {
        return queue.EnqueueAsync(message, options => options
            .WithHeader("source", "checkout")
            .WithCorrelationId(correlationId), cancellationToken);
    }
}

public sealed record ImportRequested(Guid ImportId);
```

The short overload uses the registered message contract and captures durably by default, including outside a transaction. Pass `QueueOptions` before the cancellation token for metadata or delivery overrides. Durable acceptance waits for storage, not consumer completion; restart survival requires persistent storage. Inside a compatible coordination boundary the capture commits with application state, while an incompatible boundary is rejected. Explicit `Auto` captures in a compatible boundary and sends directly with no boundary. `TransportDirect` bypasses storage and coordination and cannot be combined with `Delay`.

Omit an unused cancellation token, or pass a literal default token as `cancellationToken: default`. A bare positional `default` is ambiguous between the token and options overloads; typed token variables and explicit options remain valid.

Import `Headless.Messaging` for `QueueOptionsBuilder` and the callback extension. Its operations are `WithHeader`, `WithHeaders`, `WithCorrelationId`, `WithCausationId`, `WithMessageId`, `WithTenantId`, `WithDelay`, and `Build`. Use a callback for a single authoring scope or construct a builder directly and call `Build()` for a reusable options template. Delivery-mode and other advanced overrides remain available through `QueueOptions` or `builder.Build() with { ... }`.

Each callback runs synchronously exactly once on a fresh builder; async-void callbacks are unsupported. A null receiver or `configure` throws before user code, and a throwing callback submits nothing. The adapter forwards the original token and returns the original task. `options: null` and positional `null` keep the existing options path; `configure: null!` selects the callback guard.

Builders support sequential reuse, not concurrent mutation. Header input is copied immediately and again on each `Build()`: ordinal keys, last-write-wins merges, distinct casing, and null values are preserved. No header call leaves `Headers` null; an empty supplied collection creates an empty dictionary. Each result owns mutable headers independently. Nullable metadata and delay setters accept null to clear an explicit value. `Build()` retains `DeliveryMode.Durable` and does not validate or accept delivery; the publisher still validates headers, tenancy, and positive delays (zero is invalid).

## Configuration

None in this package. Runtime wiring is provided by `Headless.Messaging.Core` plus queue transport and storage providers.

## Dependencies

- `Headless.Messaging.Abstractions`

## Side Effects

None. This package registers no services.
