# Headless.Messaging.Storage.SqlServer

SQL Server outbox storage provider for the messaging system.

## Problem Solved

Provides durable, transactional message storage using SQL Server with automatic schema management, message archival, and optimized queries for Windows environments.

## Key Features

- **Transactional Outbox**: ACID-compliant message publishing with database changes
- **Schema Bootstrap**: Automatic table and index creation, including durable bus/queue intent columns
- **Intent-Aware Identity**: Received-message de-duplication includes version, message ID, group, and bus/queue intent
- **Archival**: Automatic cleanup of old messages
- **Performance**: Optimized indexes and queries for SQL Server
- **Monitoring**: Built-in dashboard data queries

## Installation

```bash
dotnet add package Headless.Messaging.Storage.SqlServer
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.UseSqlServer(config =>
    {
        config.ConnectionString = "Server=localhost;Database=myapp;...";
        config.Schema = "messaging";
    });

    options.UseRabbitMQ(rmq => { /* ... */ });

    options.SubscribeFromAssemblyContaining<Program>();
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

## Dependencies

- `Headless.Messaging.Core`
- `Microsoft.Data.SqlClient`

## Side Effects

- Creates database tables in configured schema:
  - `{schema}.Published` - Published messages
  - `{schema}.Received` - Received messages
  - `{schema}.Lock` - Distributed lock table
- Creates indexes for message queries
- Stores `IntentType` on published and received rows without a database default; runtime writes must provide the intent explicitly
- Periodically cleans up expired messages
