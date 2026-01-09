# Framework.Queueing.Abstractions

Defines the unified interface for queue operations.

## Problem Solved

Provides a provider-agnostic queue API with full lifecycle management (enqueue, dequeue, complete, abandon), enabling seamless switching between queue implementations without changing application code.

## Key Features

- `IQueue<T>` - Core interface for queue operations
- `IQueueEntry<T>` - Queue entry with data and metadata
- `IQueueBehavior<T>` - Extensible queue behaviors
- Async events: Enqueuing, Enqueued, Dequeued, Completed, Abandoned
- Dead letter queue support
- Queue statistics

## Installation

```bash
dotnet add package Framework.Queueing.Abstractions
```

## Usage

```csharp
public sealed class OrderProcessor(IQueue<OrderMessage> queue)
{
    public async Task EnqueueOrderAsync(OrderMessage order)
    {
        await queue.EnqueueAsync(order, new QueueEntryOptions
        {
            CorrelationId = order.Id.ToString()
        });
    }

    public async Task ProcessOrdersAsync(CancellationToken ct)
    {
        await queue.StartAsync(async (entry, token) =>
        {
            await ProcessAsync(entry.Value);
        }, autoComplete: true, ct);
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

- `Framework.Base`

## Side Effects

None.
