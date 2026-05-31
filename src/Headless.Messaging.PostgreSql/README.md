# Headless.Messaging.PostgreSql

PostgreSQL outbox storage provider for the messaging system.

## Problem Solved

Provides durable, transactional message storage using PostgreSQL with automatic schema management, message archival, and high-performance queries.

## Key Features

- **Transactional Outbox**: ACID-compliant message publishing with database changes
- **Schema Bootstrap**: Automatic table and index creation, including durable bus/queue intent columns
- **Intent-Aware Identity**: Received-message de-duplication includes version, message ID, group, and bus/queue intent
- **Archival**: Automatic cleanup of old messages
- **Performance**: Optimized indexes and queries for high throughput
- **Monitoring**: Built-in dashboard data queries

## Installation

```bash
dotnet add package Headless.Messaging.PostgreSql
```

## Quick Start

```csharp
builder.Services.ForMessagesFromAssemblyContaining<Program>();

builder.Services.AddHeadlessMessaging(options =>
{
    options.UsePostgreSql(config =>
    {
        config.ConnectionString = "Host=localhost;Database=myapp;...";
        config.Schema = "messaging";
    });

    options.UseRabbitMQ(rmq => { /* ... */ });
});
```

## Configuration

```csharp
options.UsePostgreSql(config =>
{
    config.ConnectionString = "connection_string";
    config.Schema = "messaging";
});
```

## Dependencies

- `Headless.Messaging.Core`
- `Npgsql`

## Side Effects

- Creates database tables in configured schema:
  - `{schema}.published` - Published messages
  - `{schema}.received` - Received messages
  - `{schema}.lock` - Distributed lock table
- Creates indexes for message queries
- Stores `IntentType` on published and received rows without a database default; runtime writes must provide the intent explicitly
- Periodically cleans up expired messages
