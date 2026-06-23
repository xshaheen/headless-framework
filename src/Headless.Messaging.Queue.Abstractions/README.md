# Headless.Messaging.Queue.Abstractions

Point-to-point publisher contracts for Headless Messaging.

## Problem Solved

Gives application code a compile-time queue surface for work-queue delivery where exactly one competing worker handles each message.

## Key Features

- `IQueue` enqueues directly to the configured queue transport.
- `IOutboxQueue` persists messages first, then drains them through the configured queue transport.
- `EnqueueOptions.Delay` schedules delayed outbox queue delivery.
- Every queue enqueue carries `IntentType.Queue` through storage, tracing, dashboard projections, and consume context.

## Installation

```bash
dotnet add package Headless.Messaging.Queue.Abstractions
```

## Quick Start

```csharp
public sealed class ImportJobs(IOutboxQueue queue)
{
    public Task EnqueueAsync(ImportRequested message, CancellationToken cancellationToken)
    {
        return queue.EnqueueAsync(message, new EnqueueOptions { MessageName = "imports.requested" }, cancellationToken);
    }
}
```

Use `IQueue` only when transport-direct, fire-and-forget delivery is acceptable. Use `IOutboxQueue` when the enqueue must survive process crashes or coordinate with an application transaction.

## Configuration

None in this package. Runtime wiring is provided by `Headless.Messaging.Core` plus a provider that registers `IQueueTransport`; without that provider, `IQueue` and `IOutboxQueue` are not registered.

## Dependencies

- `Headless.Messaging.Abstractions`

## Side Effects

None. This package registers no services.
