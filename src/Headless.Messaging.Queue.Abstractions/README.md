# Headless.Messaging.Queue.Abstractions

Point-to-point publisher contracts for Headless Messaging.

## Problem Solved

Gives application code a compile-time queue surface for work-queue delivery where exactly one competing worker handles each message.

## Key Features

- `IQueue` is the only queue publisher; `EnqueueOptions.DeliveryMode` selects Auto, Durable, or TransportDirect.
- Durable delivery persists messages first, then drains them through the configured queue transport.
- `EnqueueOptions.Delay` schedules durable queue delivery.
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
        return queue.EnqueueAsync(
            message,
            new EnqueueOptions { MessageName = "imports.requested", DeliveryMode = DeliveryMode.Durable },
            cancellationToken
        );
    }
}
```

Use `DeliveryMode.Durable` when the enqueue must survive process crashes, `TransportDirect` for explicit fire-and-forget delivery, or the default `Auto` to capture only in a compatible coordination boundary.

## Configuration

None in this package. Runtime wiring is provided by `Headless.Messaging.Core` plus queue transport and storage providers.

## Dependencies

- `Headless.Messaging.Abstractions`

## Side Effects

None. This package registers no services.
