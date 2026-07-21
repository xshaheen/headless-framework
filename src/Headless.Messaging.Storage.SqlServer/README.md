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

Fresh dispatch, retry pickup, and delayed scheduling atomically compare and stamp ownership from one SQL Server clock snapshot. Delayed scheduling uses ordered `UPDLOCK, READPAST` claiming, commits the transition to `Queued`, and only then returns winner messages for local enqueue.

## Installation

```bash
dotnet add package Headless.Messaging.Storage.SqlServer
```

For `UseEntityFramework<TContext>()` and the automatically coordinated transactional outbox, also install `Headless.Messaging.Storage.SqlServer.EntityFramework`.

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.ForMessagesFromAssemblyContaining<Program>();
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

```csharp
options.UseSqlServer(config =>
{
    config.ConnectionString = "connection_string";
    config.Schema = "messaging";
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

## Side Effects

- Creates database tables in configured schema:
  - `{schema}.Published` - Published messages
  - `{schema}.Received` - Received messages
  - `{schema}.Lock` - Distributed lock table
- Uses SQL Server `uniqueidentifier` primary keys and a `uniqueidentifier` ID-list table type for message row IDs
- Creates indexes for message queries
- Stores `IntentType` on published and received rows without a database default; runtime writes must provide the intent explicitly
- Periodically cleans up expired messages
