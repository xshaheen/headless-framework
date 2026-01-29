# Headless.Caching.Memory

In-memory cache implementation for single-instance applications.

## Problem Solved

Provides high-performance in-memory caching using the unified `ICache` abstraction, suitable for single-instance deployments or as an L1 cache layer.

## Key Features

- Full `IInMemoryCache` implementation
- Can serve as default `ICache` or alongside distributed cache
- Supports strongly-typed `ICache<T>` pattern
- Automatic memory management with configurable limits (MaxItems + LRU eviction)
- Can act as `IDistributedCache` adapter for single-instance scenarios
- Optional value cloning for isolation

## Installation

```bash
dotnet add package Headless.Caching.Memory
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// As default cache
builder.Services.AddInMemoryCache();

// Or with options
builder.Services.AddInMemoryCache(options =>
{
    options.MaxItems = 10000;
    options.CloneValues = true;
});

// As non-default (use alongside distributed cache)
builder.Services.AddInMemoryCache(isDefault: false);
```

## Configuration

### Options

```csharp
options.MaxItems = 10000;       // Maximum cached items (LRU eviction when exceeded)
options.CloneValues = false;    // Clone values on get/set for isolation
options.KeyPrefix = "myapp:";   // Optional key prefix
```

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Hosting`

## Side Effects

- Registers `IInMemoryCache` as singleton
- Registers `ICache` as singleton (if isDefault: true)
- Registers `IDistributedCache` adapter (if isDefault: true)
- Registers `ICache<T>` and `IInMemoryCache<T>` as singletons
