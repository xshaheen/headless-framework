# Headless.Caching.Foundatio.Redis

Redis distributed cache implementation using Foundatio for multi-instance applications.

## Problem Solved

Provides distributed caching using Redis via the unified `ICache` abstraction, enabling cache sharing across multiple application instances.

## Key Features

- Full `IDistributedCache` implementation using Foundatio.Redis
- Built on StackExchange.Redis
- Supports strongly-typed `IDistributedCache<T>` pattern
- Prefix-based key management
- Atomic operations (increment, compare-and-swap)
- Set operations

## Installation

```bash
dotnet add package Headless.Caching.Foundatio.Redis
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
- `Foundatio.Redis`

## Side Effects

- Registers `IDistributedCache` as singleton
- Registers `IDistributedCache<T>` as singleton
- Uses existing `IConnectionMultiplexer` if registered, otherwise creates one
