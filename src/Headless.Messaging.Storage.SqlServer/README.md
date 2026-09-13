# Headless.Messaging.Storage.SqlServer

SQL Server outbox storage provider for the messaging system.

## Problem Solved

Provides durable raw ADO.NET message storage using SQL Server with automatic schema management, message archival, and optimized queries for Windows environments.

## Key Features

- **Provider-neutral storage**: no EF Core or commit-coordination dependency
- **Schema Bootstrap**: Creates the final table and index shape directly, including durable bus/queue intent columns and `([StatusName],[Added])` dashboard indexes; it does not carry legacy migration DDL
- **GUID Row IDs**: Message storage identifiers come from the `SqlServer` keyed `IGuidGenerator` and are persisted as SQL Server `uniqueidentifier` columns
- **Intent-Aware Identity**: Received-message de-duplication includes version, message ID, group, and bus/queue intent
- **Archival**: Automatic cleanup of old messages
- **Performance**: Optimized indexes and queries for SQL Server
- **Monitoring**: Built-in dashboard data queries

Fresh dispatch, retry pickup, and delayed scheduling atomically compare and stamp ownership from one SQL Server clock snapshot. Delayed scheduling uses ordered `UPDLOCK, READPAST` claiming, commits the transition to `Queued`, and only then returns winner messages for local enqueue. Circuit-open received retries atomically advance `NextRetryAt` and clear only the exact live `(lane, Owner, LockedUntil)` lease generation using SQL Server's authoritative clock and null-safe owner matching.

The raw provider declares `DurableDedupeOnly`: inbox state survives restart but does not commit with application state. Terminal generations retain identity for 30 days by default, with `InboxRetention(...)` per consumer. Expiry or authorized purge resets identity; force reprocessing records linked replay provenance.

Direct admission suppresses duplicates while its root is retained. After that root expires or is purged, a new admission starts a fresh lifecycle, even when older replay descendants remain held. Replay generations increment within their own lifecycle and retain their parent incarnation; they cannot collide with a new lifecycle or an explicitly admitted generation. Holds and operation receipts continue to identify exact incarnations.

Inbox schema version 4 requires lifecycle identity and separate root/replay uniqueness. Startup rejects retained inbox rows from an older schema that lacks lifecycle identity; export or reset those unreleased-schema rows before retrying initialization. Empty schemas are initialized automatically.

Recovery of an unreadable inbox envelope records a terminal failure and clears the attempt fence in the claim transaction. Terminal retention starts from the database clock using the row’s persisted retention duration. Terminal redeliveries are suppressed without deserializing or replacing the retained payload; expiry then allows fresh admission.

## Installation

```bash
dotnet add package Headless.Messaging.Storage.SqlServer
```

For `UseEntityFramework<TContext>()` and the automatically coordinated transactional outbox, also install `Headless.Messaging.Storage.SqlServer.EntityFramework`.

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.Bus.ForMessage<OrderPlaced>(message =>
        message.Consumer<OrderPlacedConsumer>(consumer =>
            consumer.ConsumerIdentity("orders.order-placed")
        )
    );
    options.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
    options.UseSqlServer(config =>
    {
        config.ConnectionString = "Server=localhost;Database=myapp;...";
        config.Schema = "messaging";
    });

    options.UseRabbitMq(rmq =>
    { /* ... */
    });
});
```

## Configuration

Known orphans use a separate bounded probe batch and recover only when the exact consumer identity, logical contract name/version, and lane return. They do not expire automatically. Unclaimed orphans permit Hold/ReleaseHold and unheld Purge; live claims block those actions, and ForceReprocess remains terminal-only. A hold protects retention and purge but does not stop recovery.

History retention uses the shared `MessagingOptions` defaults: cleanup receipts/audits 7 days each, operator receipts 30 days, and operator audits 90 days. All four are positive configurable minimum residence durations. Audit references can extend receipt lifetime; deleting history does not release holds. SQL Server database time controls history age. Initialization adds the history-selection and audit-reference indexes idempotently. See the [Core lifecycle and retention contract](https://www.nuget.org/packages/Headless.Messaging.Core#readme-body-tab) for probe settings, replay limits, rollout effects, and collector pacing.

On an upgraded schema, the history tables (`InboxOperationReceipts`, `InboxAudit`) already hold their backlog. The first startup builds their history indexes offline, one command per index. Because `ONLINE = ON` depends on the SQL Server edition, the builds do not use it. A build holds a shared lock that blocks writes to that history table until it finishes. It runs while the initializer lock is held, and inbox readiness is published only after it finishes. Other replicas wait on that lock rather than fail. On a large backlog, pre-create the indexes during a maintenance window or allow for a longer first startup. `DdlCommandTimeout` bounds these builds and the lock wait.

```csharp
options.UseSqlServer(config =>
{
    config.ConnectionString = "connection_string";
    config.Schema = "messaging";

    // Optional: cap schema-init DDL that scales with table size (history-index builds and the
    // initializer-lock wait). Default null = no timeout (wait indefinitely), decoupled from the OLTP
    // MessagingOptions.CommandTimeout so an upgrade index build is not killed at ~30s on every boot.
    config.DdlCommandTimeout = TimeSpan.FromMinutes(30);
});
```

`UseSqlServer` ships the standard provider registration overloads: a connection string,
an `IConfiguration` section bound to `SqlServerOptions`, an `Action<SqlServerOptions>`,
and an `Action<SqlServerOptions, IServiceProvider>` (resolve secrets/connection settings from DI).
The transactional-outbox auto-wiring applies only to the `UseEntityFramework<TContext>()` path.

## Dependencies

- `Headless.Messaging.Core`
- `Microsoft.Data.SqlClient`

## SQL Server Compatibility

Dead-owner retry recovery binds live Coordination owners as ordinary SQL parameters and does not require `OPENJSON` or SQL Server compatibility level 130. Older SQL Server-compatible engines still recover through the per-row `LockedUntil` floor if reclaim fails.

Monitoring, expiry cleanup, and delayed scheduling preserve skip-locked behavior with `READ_COMMITTED_SNAPSHOT` enabled or disabled, including when pooled connections retain Serializable isolation after inbox admission. Received-message cleanup and delayed scheduling explicitly use ReadCommitted transactions; inbox admission retains its Serializable boundary.

## Side Effects

- Creates database tables in configured schema:
  - `{schema}.Published` - Published messages
  - `{schema}.Received` - Received messages
  - `{schema}.Lock` - Distributed lock table
- Uses SQL Server `uniqueidentifier` primary keys and a `uniqueidentifier` ID-list table type for message row IDs
- Creates indexes for message queries
- Stores `IntentType` on published and received rows without a database default; runtime writes must provide the intent explicitly
- Periodically cleans up expired messages
