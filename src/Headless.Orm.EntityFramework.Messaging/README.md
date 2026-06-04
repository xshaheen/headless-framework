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

- **Option A enlistment.** The dispatcher attaches the EF `IDbContextTransaction` to a fresh transient `IOutboxTransaction` with `AutoCommit = false`, publishes (rows are buffered and written inside the transaction rather than sent to the broker in-band), and detaches in `finally` without disposing — the EF pipeline owns the transaction's commit and dispose lifecycle. Outbox rows therefore commit atomically with the business data.
- **Post-commit delivery differs by provider.** On SQL Server the rows flush to the broker via the connection-commit ADO.NET diagnostic. On PostgreSQL the background relay dispatches them, which adds latency bounded by the relay interval. Pick the storage provider on `AddHeadlessMessaging` with that trade-off in mind.
- **Dependency isolation.** Keeping the implementation here leaves `Headless.Orm.EntityFramework` free of any messaging dependency — this bridge is the only messaging-aware seam between the two domains.
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
