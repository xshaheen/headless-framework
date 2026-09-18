# Headless.Messaging.Abstractions

Core contracts shared by the messaging runtime, transport providers, storage providers, and consumers.

## Problem Solved

Defines the stable message envelope, consume context, consumer contract, publisher contracts, and common options used by the intent-specific bus and queue packages.

## Key Features

- `IMessageRevoker` deletes a scheduled row by `PublishReceipt.StorageId` before its first dispatch reservation. It returns `Revoked`, `NotFound`, or `AttemptReserved`, retains no audit record, and is not tenant-scoped. Use Jobs for keyed, replaceable, tenant-scoped, or transactional deadlines.
- `PublishReceipt` carries the resolved wire `MessageId` and nullable durable `StorageId`. Direct delivery returns no storage handle. Middleware suppression before terminal publication returns both values null. A coordinated receipt remains subject to transaction commit or rollback and never implies consumer completion.
- `IConsume<TMessage>` with `ConsumeContext<TMessage>` for type-safe handlers.
- `MessageLane` for broadcast bus versus point-to-point queue delivery.
- `Message`, `TransportMessage`, headers, and publish option base types.
- `ConsumeContext.ContractVersion` exposes the logical message contract's schema version; a missing header defaults to `"1"` for legacy or external producers.
- `ConsumeContext.CorrelationId` identifies the chain root and is preserved by publishes inside a handler; `ConsumeContext.CausationId` identifies the immediate parent message when available.
- `IRuntimeSubscriber` for scoped runtime delegate subscriptions.
- Verb-specific publisher contracts: `IBus` and `IQueue`, with immutable delivery-mode options.
- `MessageOptions.SuppressAmbientBusinessContext` preserves captured business metadata by disabling ambient correlation, causation, and tenant defaults. It defaults to `false`; explicit options, registered contract/selector resolution, and diagnostic trace propagation remain unchanged. Required tenancy still rejects a null explicit tenant when suppression is enabled.

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
            context.Lane
        );

        return ValueTask.CompletedTask;
    }
}
```

Use `Headless.Messaging.Bus.Abstractions` for broadcast publisher contracts and `Headless.Messaging.Queue.Abstractions` for point-to-point publisher contracts.

`DeliveryMode` has two values. `Durable` (default) stores first — inside the caller's active unit of work when its resource is compatible and the message's `TransactionEnlistment` allows it, standalone otherwise — and `Direct` bypasses storage and the unit of work entirely and cannot be combined with `Delay` or `ScheduledAt`. Whether a durable publish enlists is the separate `TransactionEnlistment` axis (`WhenAvailable`, `Required`, `Never`; see [Unit of Work](../../docs/llms/unit-of-work.md)). Precedence for both is per call, then per type (`WithDeliveryMode` / `WithEnlistment`), then `MessagingOptions.DefaultDeliveryMode` / `DefaultEnlistment`. The full guarantee matrix lives in [Delivery Modes](../../docs/llms/messaging.md#delivery-modes).

## Callbacks

Callbacks are fire-and-forget async chaining, not request/reply. The publisher sets `PublishOptions.CallbackName` (or `QueueOptions.CallbackName`) on the request; the consumer shapes the response through two `ConsumeContext` methods:

- `context.SetResponse<TResponse>(value)` — capture a typed response body to publish to the request's callback message name through the durable bus path. `TResponse` must be a reference type (`where TResponse : class`); wrap value types in a record if needed. No `SetResponse` keeps the callback headers-only; `SetResponse` without a `CallbackName` is dropped.
- `context.SetResponseCallbackName(callbackName)` — stamp the response callback name the published response will carry, enabling explicit multi-hop chaining (typed alternative to writing the reserved `CallbackName` key through `AddResponseHeader`).

Callback delivery is at-least-once — make response consumers idempotent (for example, dedupe on `(CausationId, CorrelationSequence)`). `CorrelationId` identifies the chain root; `CausationId` identifies the immediate parent message. The framework does not deduplicate callback deliveries.

## Configuration

`MessageOptions.RoutingAffinityKey` is an optional provider-neutral string. `TransportMessage.RoutingAffinityKey` reads its reserved `headless-routing-affinity-key` envelope header. Use the typed option; custom writes to that neutral header are rejected. Null preserves unkeyed behavior. Nonempty keys must satisfy the configured provider bounds and raw provider adapters must agree with the typed value.

None. This package only defines contracts.

Retry configuration is owned by `Headless.Messaging.Core`, which exposes Polly.Core contracts directly. This abstractions package intentionally defines no retry strategy or decision wrapper.

## Dependencies

- None beyond the .NET runtime surface.

## Side Effects

None. This package registers no services.
