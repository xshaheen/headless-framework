# Headless.Messaging.Storage.PostgreSql

PostgreSQL outbox storage provider for the messaging system.

## Problem Solved

Provides durable raw ADO.NET message storage using PostgreSQL with automatic schema management, message archival, and high-performance queries.

## Key Features

- **Provider-neutral storage**: no EF Core or commit-coordination dependency
- **Schema Bootstrap**: Automatic table and index creation, including durable bus/queue intent columns
- **GUID Row IDs**: Message storage identifiers come from the `Version7` keyed `IGuidGenerator` and are persisted as PostgreSQL `UUID` columns
- **Intent-Aware Identity**: Received-message de-duplication includes version, message ID, group, and bus/queue intent
- **Archival**: Automatic cleanup of old messages
- **Performance**: Optimized indexes and queries for high throughput
- **Monitoring**: Built-in dashboard data queries

Fresh dispatch, retry pickup, and delayed scheduling atomically compare and stamp ownership from one PostgreSQL clock snapshot. Delayed scheduling uses ordered `FOR UPDATE SKIP LOCKED` claiming, commits the transition to `Queued`, and only then returns winner messages for local enqueue. Circuit-open received retries atomically advance `NextRetryAt` and clear only the exact live `(lane, Owner, LockedUntil)` lease generation using PostgreSQL's authoritative clock and null-safe owner matching.

The raw provider declares `DurableDedupeOnly`: inbox state survives restart but does not commit with application state. Terminal generations retain identity for 30 days by default, with `InboxRetention(...)` per consumer. Expiry or authorized purge resets identity; force reprocessing records linked replay provenance.

Direct admission suppresses duplicates while its root is retained. After that root expires or is purged, a new admission starts a fresh lifecycle, even when older replay descendants remain held. Replay generations increment within their own lifecycle and retain their parent incarnation; they cannot collide with a new lifecycle or an explicitly admitted generation. Holds and operation receipts continue to identify exact incarnations.

Inbox schema version 4 requires lifecycle identity and separate root/replay uniqueness. Startup rejects retained inbox rows from an older schema that lacks lifecycle identity; export or reset those unreleased-schema rows before retrying initialization. Empty schemas are initialized automatically.

Recovery of an unreadable inbox envelope records a terminal failure and clears the attempt fence in the claim transaction. Terminal retention starts from the database clock using the row’s persisted retention duration. Terminal redeliveries are suppressed without deserializing or replacing the retained payload; expiry then allows fresh admission.

## Installation

```bash
dotnet add package Headless.Messaging.Storage.PostgreSql
```

For `UseEntityFramework<TContext>()` and the automatically coordinated transactional outbox, also install `Headless.Messaging.Storage.PostgreSql.EntityFramework`.

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
    options.UsePostgreSql(config =>
    {
        config.ConnectionString = "Host=localhost;Database=myapp;...";
        config.Schema = "messaging";
    });

    options.UseRabbitMq(rmq =>
    { /* ... */
    });
});
```

## Configuration

```csharp
options.UsePostgreSql(config =>
{
    config.ConnectionString = "connection_string";
    config.Schema = "messaging";

    // Optional: cap schema-init DDL (CREATE/DROP INDEX CONCURRENTLY, the CREATE EXTENSION probe, and the
    // advisory-lock waits that gate them). Default null = no timeout (wait indefinitely), decoupled from
    // the OLTP MessagingOptions.CommandTimeout so a large-table index build at startup is not killed at
    // ~30s (which would leave the CONCURRENTLY index INVALID for the next boot to repair).
    config.DdlCommandTimeout = TimeSpan.FromMinutes(30);
});
```

`UsePostgreSql` ships the standard provider registration overloads: a connection string,
an `IConfiguration` section bound to `PostgreSqlOptions`, an `Action<PostgreSqlOptions>`,
and an `Action<PostgreSqlOptions, IServiceProvider>` (resolve secrets/connection settings from DI).
The transactional-outbox auto-wiring applies only to the `UseEntityFramework<TContext>()` path.

### `pg_trgm` on managed PostgreSQL

Dashboard content (ILIKE) search is accelerated by GIN trigram indexes that require the `pg_trgm`
extension. The initializer runs `CREATE EXTENSION IF NOT EXISTS pg_trgm` **best-effort, outside** the
schema transaction. On managed PostgreSQL (AWS RDS, Azure Database for PostgreSQL, Neon, Supabase) the
application role usually lacks `CREATE EXTENSION`; the initializer logs a warning, **skips the trigram
content indexes**, and continues — all write/retry paths initialize normally, only dashboard content
search is unavailable. A DBA/superuser can pre-install it (`CREATE EXTENSION pg_trgm;`) and it is picked
up automatically on the next startup.

## Dependencies

- `Headless.Messaging.Core`
- `Npgsql`

## Side Effects

- Creates database tables in configured schema:
  - `{schema}.published` - Published messages
  - `{schema}.received` - Received messages
- Uses PostgreSQL `UUID` primary keys for message row IDs
- Creates the final index shape directly (including `("StatusName","Added")` for dashboard timelines and a partial `("Version","ExpiresAt") WHERE "StatusName" = 'Queued'` index for the delayed scheduler); schema initialization does not alter legacy columns or drop superseded indexes
- Best-effort `CREATE EXTENSION pg_trgm` for dashboard content search; trigram indexes are skipped when the extension is unavailable
- Stores `IntentType` on published and received rows without a database default; runtime writes must provide the intent explicitly
- Periodically cleans up expired messages
