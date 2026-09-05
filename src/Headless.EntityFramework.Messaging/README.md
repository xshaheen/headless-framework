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
- **Custom dispatchers.** Both `IHeadlessOutboxDispatcher` methods receive `IReadOnlyList<EventContext<object>>`. Serialize `context.Payload` while preserving `context.EventId`, correlation, causation, and tenant; dispatch never recaptures identity.
- **Commit-coordinated enlistment.** The save pipeline opens its transaction and synchronously enlists it in commit coordination (`DatabaseFacade.EnlistCommitCoordination`), so the ambient commit coordinator carries the live transaction. The dispatcher publishes each integration event; the outbox writer buffers the rows inside the transaction — not sent to the broker in-band. The registered `IDbTransactionInterceptor` drains the buffered dispatch on commit and discards it on rollback. Outbox rows commit atomically with the business data.
- **Post-commit delivery.** The interceptor triggers the buffered dispatch on commit; the background relay also sweeps committed rows independently for crash recovery. On PostgreSQL the relay is the primary latency-bounded path. Pick the outbox storage provider on `AddHeadlessMessaging` with that trade-off in mind.
- **Dependency isolation.** This bridge stays the only messaging-aware seam between the two domains and selects `Headless.EntityFramework.CommitCoordination`. Core `Headless.EntityFramework` depends on neither messaging nor commit coordination.
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
- `Headless.EntityFramework.CommitCoordination`
- `Headless.Domain`
- `Headless.Messaging.Bus.Abstractions`
- `Headless.Messaging.Abstractions`

## Side Effects

- Registers `IHeadlessOutboxDispatcher` as scoped (`TryAdd`) — `OutboxIntegrationEventDispatcher`
- Registers `IntegrationEventPublishInvokerCache` as singleton (`TryAdd`)
- Selects `Headless.EntityFramework.CommitCoordination` for the save pipeline
