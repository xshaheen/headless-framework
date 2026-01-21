# Headless.Messaging.SqlServer

SQL Server outbox storage provider for the messaging system.

## Problem Solved

Provides durable, transactional message storage using SQL Server with automatic schema management, message archival, and optimized queries for Windows environments.

## Key Features

- **Transactional Outbox**: ACID-compliant message publishing with database changes
- **Auto-Migration**: Automatic table creation and schema updates
- **Archival**: Automatic cleanup of old messages
- **Performance**: Optimized indexes and queries for SQL Server
- **Monitoring**: Built-in dashboard data queries

## Installation

```bash
dotnet add package Headless.Messaging.SqlServer
```

## Quick Start

```csharp
builder.Services.AddMessages(options =>
{
    options.UseSqlServer(config =>
    {
        config.ConnectionString = "Server=localhost;Database=myapp;...";
        config.Schema = "messaging";
    });

    options.UseRabbitMQ(rmq => { /* ... */ });

    options.ScanConsumers(typeof(Program).Assembly);
});
```

## Configuration

```csharp
options.UseSqlServer(config =>
{
    config.ConnectionString = "connection_string";
    config.Schema = "messaging";
    config.TableNamePrefix = "msg";
});
```

## Dependencies

- `Headless.Messaging.Core`
- `Microsoft.Data.SqlClient`

## Side Effects

- Creates database tables in configured schema:
  - `{prefix}_published` - Published messages
  - `{prefix}_received` - Received messages
  - `{prefix}_lock` - Distributed lock table
- Creates indexes for message queries
- Periodically cleans up expired messages
