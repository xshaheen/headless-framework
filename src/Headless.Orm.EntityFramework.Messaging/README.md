# Headless.Orm.EntityFramework.Messaging

Bridge package that ships the real `IHeadlessOutboxDispatcher` so integration events emitted during EF saves are written to the messaging outbox.

## Problem Solved

`Headless.Orm.EntityFramework` defines the `IHeadlessOutboxDispatcher` seam but ships no implementation, so it carries no messaging dependency. This package supplies the implementation: integration events emitted by entities during an EF `SaveChanges` are written to the messaging outbox atomically with the business data and delivered to the broker after commit — without the core ORM package depending on messaging.

## Key Features

- Transactional outbox enlistment in the EF save transaction, so outbox rows commit atomically with the business data
- Routes each concrete `IIntegrationEvent` to `IOutboxBus.PublishAsync<TConcrete>` through a cached compiled invoker (one compiled delegate per runtime event type)
- Sync and async save paths (`Dispatch` and `DispatchAsync`)
- `.AddIntegrationEventOutbox()` builder extension on `IHeadlessDbContextBuilder`

## Design Notes

- **Commit-coordinated enlistment.** The save pipeline opens its transaction and synchronously enlists it in commit coordination (`DatabaseFacade.EnlistCommitCoordination`), so the ambient commit coordinator carries the live transaction. The dispatcher just publishes each integration event; the outbox writer writes the rows **inside** the transaction (buffered, not sent to the broker in-band). The registered EF `IDbTransactionInterceptor` drains the buffered dispatch on commit and discards it on rollback, so outbox rows commit atomically with the business data.
- **Post-commit delivery.** The interceptor triggers the buffered dispatch on commit; the background relay also sweeps committed rows independently for crash recovery (on PostgreSQL the relay is the primary latency-bounded path). Pick the storage provider on `AddHeadlessMessaging` with that trade-off in mind.
- **Dependency isolation.** This bridge stays the only messaging-aware seam between the two domains. `Headless.Orm.EntityFramework` takes a dependency on `Headless.CommitCoordination.EntityFramework` (generic, datastore-agnostic transaction coordination — not messaging) to own the coordinated save scope.
- **CDC alternative.** Change Data Capture (for example, Debezium reading the database transaction log) is an advanced alternative deployment for capturing integration events outside the application process; it bypasses this dispatcher entirely and is a host-infrastructure decision, not a package option.

## Installation

```bash
dotnet add package Headless.Orm.EntityFramework.Messaging
```

## Quick Start

```csharp
builder.Services.AddHeadlessDbContextServices()
    .AddDomainEvents()             // ILocalEventBus for in-process domain events
    .AddIntegrationEventOutbox();  // outbox dispatch for integration events

// A messaging setup with an outbox storage provider is required.
builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseInMemory();
    setup.UsePostgreSql(connectionString);
});
```

`.AddIntegrationEventOutbox()` is parameterless — the dispatcher has no options. Broker, storage, and retry behavior are configured on `AddHeadlessMessaging`. Once registered, integration events emitted by `IIntegrationEventEmitter` entities during a save are enqueued to the outbox before commit and delivered after commit.

## Configuration

`None.` (configured via `AddHeadlessMessaging`.)

## Dependencies

- `Headless.Orm.EntityFramework`
- `Headless.Domain`
- `Headless.Messaging.Bus.Abstractions`
- `Headless.Messaging.Abstractions`

## Side Effects

- Registers `IHeadlessOutboxDispatcher` (scoped, `TryAdd`)
- Registers an integration-event publish invoker cache (singleton, `TryAdd`)
