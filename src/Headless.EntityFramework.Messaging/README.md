# Headless.EntityFramework.Messaging

Bridge package that ships the real `IHeadlessOutboxDispatcher` so integration events emitted during EF saves are written to the messaging outbox atomically with the business data.

## Problem Solved

`Headless.EntityFramework` defines the `IHeadlessOutboxDispatcher` seam but ships no implementation, so it carries no messaging dependency. This package supplies the implementation: integration events emitted by entities during an EF `SaveChanges` are written to the messaging outbox atomically with the business data and delivered to the broker after commit — without the core Entity Framework package depending on messaging.

## Key Features

- Transactional outbox enlistment in the EF save transaction, so outbox rows commit atomically with the business data
- Preserves each `EventContext<object>` snapshot: `EventId` becomes Messaging `MessageId`; correlation, immediate causation, and tenant remain the values captured at emission
- Routes each concrete integration payload to durable `IBus.PublishAsync<TConcrete>` through `IntegrationEventPublishInvokerCache` — one compiled delegate per runtime event type for allocation efficiency
- Both sync (`Dispatch`) and async (`DispatchAsync`) save paths via `OutboxIntegrationEventDispatcher`
- `.AddIntegrationEventOutbox()` builder extension on `IHeadlessDbContextBuilder`

## Design Notes

- **Occurrence forwarding.** The bridge forwards captured integration occurrences and publishes their concrete payloads through Messaging's existing contract name/version resolver. Application handlers derive new facts with new occurrence IDs and the immediate Domain parent as causation; forwarding an existing occurrence keeps its ID. There is no Domain durable-contract registry.
- **Captured absence.** Each durable publish sets `SuppressAmbientBusinessContext = true`, so a captured root cause or system tenant cannot be replaced by unrelated consume/tenant state at save time. `TenantContextRequired = true` still rejects a captured null tenant. Diagnostic trace propagation and registered Messaging contracts remain independent.
- **Save and recovery.** Persistence retry within a pipeline-owned save reuses the captured IDs and completed local drain. Each successful caller-owned save clears only its saved batch; outer commit persists all staged batches, while a known outer rollback requires a fresh context and aggregate graph. An unknown commit result requires durable outcome verification or application idempotency before replay. Broker delivery and external effects remain at-least-once.
- **Direct handler publishes.** A domain handler's enlisted `IBus` write prevents execution-strategy retry (`IUnitOfWork.PreventRetry()`) because the completed occurrence will not run again to restore its rolled-back outbox row. After failure, use a fresh context and aggregate graph. Captured integration events remain replayable through this bridge.
- **Custom dispatchers.** Both `IHeadlessOutboxDispatcher` methods receive `IReadOnlyList<EventContext<object>>`. Serialize `context.Payload` while preserving `context.EventId`, correlation, causation, and tenant; dispatch never recaptures identity.
- **Unit-of-work enlistment.** The save pipeline makes its transaction the scope's current unit of work (enlisting its own transaction, or adopting one the caller began with `BeginAsync(db)`) before this dispatcher runs. The dispatcher requires a resource-bearing unit and throws before touching the bus otherwise; the scoped `IBus` reads `IUnitOfWorkManager.Current` itself, so the outbox writer buffers the rows inside the unit's transaction — not sent to the broker in-band. `IUnitOfWork.CompleteAsync` drains the buffered dispatch after commit; any rollback discards it. Outbox rows commit atomically with the business data.
- **Post-commit delivery.** `IUnitOfWork.CompleteAsync` triggers the buffered dispatch after commit; the background relay also sweeps committed rows independently for crash recovery. On PostgreSQL the relay is the primary latency-bounded path. Pick the outbox storage provider on `AddHeadlessMessaging` with that trade-off in mind.
- **Dependency isolation.** This bridge stays the only messaging-aware seam between the two domains. It references `Headless.UnitOfWork.EntityFramework` for the enlistment contract; core `Headless.EntityFramework` depends on neither messaging nor a separate commit-coordination adapter.
- **CDC alternative.** Change Data Capture (e.g. Debezium reading the database transaction log) is an advanced alternative deployment for capturing integration events outside the application process; it bypasses this dispatcher entirely and is a host-infrastructure decision, not a package option.

## Installation

```bash
dotnet add package Headless.EntityFramework.Messaging
```

## Quick Start

```csharp
// Chain after AddHeadlessDbContextServices:
builder
    .Services.AddHeadlessDbContextServices()
    .AddDomainEvents() // IDomainEventDispatcher for in-process domain events
    .AddIntegrationEventOutbox(); // IHeadlessOutboxDispatcher — this package

// A messaging setup with an outbox storage provider is required:
builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseInMemory(); // broker
    setup.UsePostgreSql(connectionString); // outbox storage
});
```

`.AddIntegrationEventOutbox()` is parameterless — the dispatcher has no options. Broker, storage, and retry behavior are configured on `AddHeadlessMessaging`. Once registered, integration events emitted by `IIntegrationEventEmitter` entities during a save are enqueued to the outbox before commit and delivered after commit.

## Configuration

None. (Configured via `AddHeadlessMessaging`.)

## Dependencies

- `Headless.EntityFramework`
- `Headless.UnitOfWork.EntityFramework`
- `Headless.Domain`
- `Headless.Messaging.Bus.Abstractions`
- `Headless.Messaging.Abstractions`

## Side Effects

- Registers `IHeadlessOutboxDispatcher` as scoped (`TryAdd`) — `OutboxIntegrationEventDispatcher` (injects the scoped `IBus` and `IUnitOfWorkManager`)
- Registers `IntegrationEventPublishInvokerCache` as singleton (`TryAdd`)
