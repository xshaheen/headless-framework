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
