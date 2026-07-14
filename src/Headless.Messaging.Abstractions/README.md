# Headless.Messaging.Abstractions

Core contracts shared by the messaging runtime, transport providers, storage providers, and consumers.

## Problem Solved

Defines the stable message envelope, consume context, consumer contract, publisher contracts, and common options used by the intent-specific bus and queue packages.

## Key Features

- `IConsume<TMessage>` with `ConsumeContext<TMessage>` for type-safe handlers.
- `IntentType` for broadcast bus versus point-to-point queue delivery.
- `Message`, `TransportMessage`, headers, and publish option base types.
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
            "Processing {OrderId} from {MessageName} with {Intent}",
            context.Message.OrderId,
            context.MessageName,
            context.IntentType
        );

        return ValueTask.CompletedTask;
    }
}
```

Use `Headless.Messaging.Bus.Abstractions` for broadcast publisher contracts and `Headless.Messaging.Queue.Abstractions` for point-to-point publisher contracts.

## Callbacks

Callbacks are fire-and-forget async chaining, not request/reply. The publisher sets `PublishOptions.CallbackName` (or `EnqueueOptions.CallbackName`) on the request; the consumer shapes the response through two `ConsumeContext` methods:

- `context.SetResponse<TResponse>(value)` — capture a typed response body to publish to the request's callback message name through the durable bus path. `TResponse` must be a reference type (`where TResponse : class`); wrap value types in a record if needed. No `SetResponse` keeps the callback headers-only; `SetResponse` without a `CallbackName` is dropped.
- `context.SetResponseCallbackName(callbackName)` — stamp the response callback name the published response will carry, enabling explicit multi-hop chaining (typed alternative to writing the reserved `CallbackName` key through `AddResponseHeader`).

Callback delivery is at-least-once — make response consumers idempotent (dedupe on `(CorrelationId, CorrelationSequence)`; `CorrelationId` alone is ambiguous across hops because it is set to the immediate parent message id per hop, not the chain root).

## Configuration

None. This package only defines contracts.

Retry configuration is owned by `Headless.Messaging.Core`, which exposes Polly.Core contracts directly. This abstractions package intentionally defines no retry strategy or decision wrapper.

## Dependencies

- None beyond the .NET runtime surface.

## Side Effects

None. This package registers no services.
