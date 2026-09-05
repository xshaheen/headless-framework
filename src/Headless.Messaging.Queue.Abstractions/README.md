# Headless.Messaging.Queue.Abstractions

Point-to-point publisher contracts for Headless Messaging.

## Problem Solved

Gives application code a compile-time queue surface for work-queue delivery where exactly one competing worker handles each message.

## Key Features

- `IQueue` is the only queue publisher; `QueueOptions.DeliveryMode` defaults to Durable; Auto and TransportDirect are explicit overrides.
- Durable delivery persists messages first, then drains them through the configured queue transport.
- `QueueOptions.Delay` schedules durable queue delivery.
- Every queue enqueue carries `MessageLane.Queue` through storage, tracing, dashboard projections, and consume context.

## Installation

```bash
dotnet add package Headless.Messaging.Queue.Abstractions
```

## Quick Start

```csharp
public sealed class ImportJobs(IQueue queue)
{
    public Task EnqueueAsync(ImportRequested message, CancellationToken cancellationToken)
    {
        return queue.EnqueueAsync(message, cancellationToken);
    }
}
```

The short overload uses the registered message contract and captures durably by default, including outside a transaction. Pass `QueueOptions` before the cancellation token for metadata or delivery overrides. Durable acceptance waits for storage, not consumer completion; restart survival requires persistent storage. Inside a compatible coordination boundary the capture commits with application state, while an incompatible boundary is rejected. Explicit `Auto` captures in a compatible boundary and sends directly with no boundary. `TransportDirect` bypasses storage and coordination and cannot be combined with `Delay`.

## Configuration

None in this package. Runtime wiring is provided by `Headless.Messaging.Core` plus queue transport and storage providers.

## Dependencies

- `Headless.Messaging.Abstractions`

## Side Effects

None. This package registers no services.
