# Headless.Messaging.Storage.PostgreSql

PostgreSQL outbox storage provider for the messaging system.

## Problem Solved

Provides durable, transactional message storage using PostgreSQL with automatic schema management, message archival, and high-performance queries.

## Key Features

- **Transactional Outbox**: ACID-compliant message publishing with database changes
- **Schema Bootstrap**: Automatic table and index creation, including durable bus/queue intent columns
- **GUID Row IDs**: Message storage identifiers come from the `Version7` keyed `IGuidGenerator` and are persisted as PostgreSQL `UUID` columns
- **Intent-Aware Identity**: Received-message de-duplication includes version, message ID, group, and bus/queue intent
- **Archival**: Automatic cleanup of old messages
- **Performance**: Optimized indexes and queries for high throughput
- **Monitoring**: Built-in dashboard data queries

## Installation

```bash
dotnet add package Headless.Messaging.Storage.PostgreSql
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.ForMessagesFromAssemblyContaining<Program>();
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
- Creates indexes for message queries (including `("StatusName","Added")` for dashboard timelines and a partial `("Version","ExpiresAt") WHERE "StatusName" = 'Queued'` index for the delayed scheduler)
- Best-effort `CREATE EXTENSION pg_trgm` for dashboard content search; trigram indexes are skipped when the extension is unavailable
- Stores `IntentType` on published and received rows without a database default; runtime writes must provide the intent explicitly
- Periodically cleans up expired messages
