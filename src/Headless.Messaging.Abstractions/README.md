# Headless.Messaging.Abstractions

Core contracts shared by the messaging runtime, transport providers, storage providers, and consumers.

## Problem Solved

Defines the stable message envelope, consume context, consumer contract, publisher contracts, retry contracts, and common options used by the intent-specific bus and queue packages.

## Key Features

- `IConsume<TMessage>` with `ConsumeContext<TMessage>` for type-safe handlers.
- `IntentType` for broadcast bus versus point-to-point queue delivery.
- `Message`, `TransportMessage`, headers, publish option base types, and retry primitives.
- `IRuntimeSubscriber` for scoped runtime delegate subscriptions.
- Intent-specific publisher contracts: `IBus`, `IQueue`, `IOutboxBus`, and `IOutboxQueue`.

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

<<<<<<< HEAD
No configuration required. This is an abstractions package. Implementations are provided by:
- `Headless.Messaging.Core` (base implementation)
- Transport packages: `Headless.Messaging.RabbitMQ`, `Headless.Messaging.Kafka`, etc.
- Storage packages: `Headless.Messaging.Storage.PostgreSql`, `Headless.Messaging.Storage.SqlServer`, etc.
||||||| parent of b25f49dc9 (feat(messaging): split bus and queue delivery intents (#340))
No configuration required. This is an abstractions package. Implementations are provided by:
- `Headless.Messaging.Core` (base implementation)
- Transport packages: `Headless.Messaging.RabbitMQ`, `Headless.Messaging.Kafka`, etc.
- Storage packages: `Headless.Messaging.PostgreSql`, `Headless.Messaging.SqlServer`, etc.
=======
None. This package only defines contracts.
>>>>>>> b25f49dc9 (feat(messaging): split bus and queue delivery intents (#340))

## Dependencies

- None beyond the .NET runtime surface.

## Side Effects

None. This package registers no services.
