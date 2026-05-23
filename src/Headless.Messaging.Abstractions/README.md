# Headless.Messaging.Abstractions

Core contracts shared by the messaging runtime, transport providers, storage providers, and consumers.

## Problem Solved

Defines the stable message envelope, consume context, consumer contract, legacy compatibility publisher contracts, retry contracts, and common options used by the intent-specific bus and queue packages.

## Key Features

- `IConsume<TMessage>` with `ConsumeContext<TMessage>` for type-safe handlers.
- `IntentType` for broadcast bus versus point-to-point queue delivery.
- `Message`, `TransportMessage`, headers, publish option base types, and retry primitives.
- `IRuntimeSubscriber` for scoped runtime delegate subscriptions.
- Legacy publisher contracts remain for compatibility; new code should choose `IBus`, `IQueue`, `IOutboxBus`, or `IOutboxQueue`.

## Installation

```bash
dotnet add package Headless.Messaging.Abstractions
```

## Quick Start

```csharp
public sealed class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger) : IConsume<OrderPlacedEvent>
{
    public ValueTask Consume(ConsumeContext<OrderPlacedEvent> context, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Processing {OrderId} from {Topic} with {Intent}",
            context.Message.OrderId,
            context.Topic,
            context.IntentType);

        return ValueTask.CompletedTask;
    }
}
```

Use `Headless.Messaging.Bus.Abstractions` for broadcast publisher contracts and `Headless.Messaging.Queue.Abstractions` for point-to-point publisher contracts.

## Configuration

None. This package only defines contracts.

## Dependencies

- None beyond the .NET runtime surface.

## Side Effects

None. This package registers no services.
