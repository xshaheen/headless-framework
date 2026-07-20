# Headless.EntityFramework.Messaging

Bridge package that ships the real `IHeadlessOutboxDispatcher` so integration events emitted during EF saves are written to the messaging outbox atomically with the business data.

## Problem Solved

`Headless.EntityFramework` defines the `IHeadlessOutboxDispatcher` seam but ships no implementation, so it carries no messaging dependency. This package supplies the implementation: integration events emitted by entities during an EF `SaveChanges` are written to the messaging outbox atomically with the business data and delivered to the broker after commit — without the core Entity Framework package depending on messaging.

## Key Features

- Transactional outbox enlistment in the EF save transaction, so outbox rows commit atomically with the business data
- Routes each concrete `IIntegrationEvent` to `IOutboxBus.PublishAsync<TConcrete>` through `IntegrationEventPublishInvokerCache` — one compiled delegate per runtime event type for allocation efficiency
- Both sync (`Dispatch`) and async (`DispatchAsync`) save paths via `OutboxIntegrationEventDispatcher`
- `.AddIntegrationEventOutbox()` builder extension on `IHeadlessDbContextBuilder`

## Design Notes

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
    .AddDomainEvents() // ILocalEventBus for in-process domain events
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
