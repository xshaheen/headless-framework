# Headless.DistributedLocks.Cache

Cache-based resource lock storage using ICache.

## Problem Solved

Provides resource lock storage using the headless's `ICache` abstraction, suitable for single-instance deployments or when using a distributed cache like Redis.

## Key Features

- `CacheDistributedLockStorage` - Lock storage via `ICache`
- `CacheThrottlingDistributedLockStorage` - Throttling lock storage
- Automatic expiration via cache TTL

## Installation

```bash
dotnet add package Headless.DistributedLocks.Cache
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add cache (e.g., Redis cache)
builder.Services.AddRedisCache(options => { /* ... */ });

// Add resource locks with cache storage
builder.Services.AddDistributedLock();
builder.Services.AddSingleton<IDistributedLockStorage, CacheDistributedLockStorage>();
```

## Configuration

No additional configuration required.

## Dependencies

- `Headless.DistributedLocks.Core`
- `Headless.Caching.Abstractions`

## Side Effects

- Registers `IDistributedLockStorage` as singleton
- Registers `IThrottlingDistributedLockStorage` as singleton (optional)
