# Headless.Caching.Redis

Redis distributed cache implementation for multi-instance applications.

## Problem Solved

Provides distributed caching using Redis via the unified `ICache` abstraction, enabling cache sharing across multiple application instances.

## Key Features

- Full `IDistributedCache` implementation using StackExchange.Redis
- Supports strongly-typed `IDistributedCache<T>` pattern
- Prefix-based key management
- Atomic operations (increment, compare-and-swap, SetIfHigher/Lower)
- Set/list operations with pagination
- Lua scripts for atomic multi-key operations
- Redis Cluster support

## Installation

```bash
dotnet add package Headless.Caching.Redis
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Option 1: Use connection string
builder.Services.AddRedisCaching(options =>
{
    options.ConnectionString = "localhost:6379";
});

// Option 2: Use existing IConnectionMultiplexer
builder.Services.AddRedisCaching();
```

## Configuration

### appsettings.json

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379,password=secret,ssl=true"
  }
}
```

### Options

```csharp
options.ConnectionString = "localhost:6379";
options.KeyPrefix = "myapp:";
```

## Cancellation Token Behavior

Cancellation is checked **at the start** of each operation. Once an operation begins, it completes without interruption:

| Operation Type | Cancellation Behavior |
|---------------|----------------------|
| Single-key ops (`GetAsync`, `UpsertAsync`, etc.) | Checked before Redis call; operation completes atomically |
| Batch ops (`UpsertAllAsync`, `RemoveAllAsync`) | Checked before batch starts; all keys processed atomically |
| SCAN-based ops (`RemoveByPrefixAsync`, `GetAllKeysByPrefixAsync`) | Cancellable during iteration (unbounded key sets) |

This design ensures consumers never observe partial results from batch operations.

> **Note:** StackExchange.Redis doesn't support `CancellationToken` in its API. Timeouts are configured via `ConfigurationOptions.SyncTimeout` and `AsyncTimeout`.

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Hosting`
- `Headless.Redis`
- `Headless.Serializer.Json`
- `StackExchange.Redis`

## Side Effects

- Registers `IDistributedCache` as singleton
- Registers `IDistributedCache<T>` as singleton
- Uses existing `IConnectionMultiplexer` if registered, otherwise creates one
