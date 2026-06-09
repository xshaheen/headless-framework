# Headless.Caching.InMemory

In-memory cache implementation for single-instance applications.

## Problem Solved

Provides high-performance in-memory caching using the unified `ICache` abstraction, suitable for single-instance deployments or as an L1 cache layer.

## Key Features

- Full `IInMemoryCache` implementation
- Can serve as default `ICache` or alongside distributed cache
- Supports strongly-typed `ICache<T>` pattern
- Automatic memory management with configurable limits (MaxItems + LRU eviction)
- Can act as `IRemoteCache` adapter for single-instance scenarios
- Optional value cloning for isolation

## Design Notes

Memory cache stores entries in an internal envelope with logical expiration, physical expiration, and optional sliding expiration. Direct writes set logical and physical timestamps equal. Fail-safe `GetOrAddAsync` can make physical expiration outlive logical expiration so a stale reserve stays in memory after normal value reads miss. Sliding `GetOrAddAsync` keeps physical expiration as the absolute cap and re-arms logical expiration on value reads only; `GetExpirationAsync`, `ExistsAsync`, key listing, and count operations do not extend the idle window. Physical expiration still drives eviction, LRU maintenance, size compaction, `GetCountAsync`, and key listing. Logical expiration drives `GetAsync`, `GetAllAsync`, `GetByPrefixAsync`, `GetSetAsync`, `ExistsAsync`, and `GetExpirationAsync`.

Long `FailSafeMaxDuration` values and long sliding absolute caps can retain more entries in process memory. Use `MaxItems`, `MaxMemorySize`, and LRU compaction to bound direct in-memory deployments.

## Installation

```bash
dotnet add package Headless.Caching.InMemory
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
- `Headless.Caching.Core`
- `Headless.Hosting`

## Side Effects

- Registers `IInMemoryCache` as singleton
- Registers `ICache` as singleton (if isDefault: true)
- Registers `IRemoteCache` adapter (if isDefault: true)
- Registers `ICache<T>` and `IInMemoryCache<T>` as singletons
