# Framework.Messages.PostgreSql

PostgreSQL outbox storage provider for the messaging system.

## Problem Solved

Provides durable, transactional message storage using PostgreSQL with automatic schema management, message archival, and high-performance queries.

## Key Features

- **Transactional Outbox**: ACID-compliant message publishing with database changes
- **Auto-Migration**: Automatic table creation and schema updates
- **Archival**: Automatic cleanup of old messages
- **Performance**: Optimized indexes and queries for high throughput
- **Monitoring**: Built-in dashboard data queries

## Installation

```bash
dotnet add package Framework.Messages.PostgreSql
```

## Quick Start

```csharp
builder.Services.AddMessages(options =>
{
    options.UsePostgreSql(config =>
    {
        config.ConnectionString = "Host=localhost;Database=myapp;...";
        config.Schema = "messaging";
    });

    options.UseRabbitMQ(rmq => { /* ... */ });

    options.ScanConsumers(typeof(Program).Assembly);
});
```

## Configuration

```csharp
options.UsePostgreSql(config =>
{
    config.ConnectionString = "connection_string";
    config.Schema = "messaging";
    config.TableNamePrefix = "msg";
});
```

## Dependencies

- `Framework.Messages.Core`
- `Npgsql`

## Side Effects

- Creates database tables in configured schema:
  - `{prefix}_published` - Published messages
  - `{prefix}_received` - Received messages
  - `{prefix}_lock` - Distributed lock table
- Creates indexes for message queries
- Periodically cleans up expired messages
