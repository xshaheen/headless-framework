# Framework.ResourceLocks.Cache

Cache-based resource lock storage using ICache.

## Problem Solved

Provides resource lock storage using the framework's `ICache` abstraction, suitable for single-instance deployments or when using a distributed cache like Redis.

## Key Features

- `CacheResourceLockStorage` - Lock storage via `ICache`
- `CacheThrottlingResourceLockStorage` - Throttling lock storage
- Automatic expiration via cache TTL

## Installation

```bash
dotnet add package Framework.ResourceLocks.Cache
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add cache (e.g., Redis cache)
builder.Services.AddRedisCache(options => { /* ... */ });

// Add resource locks with cache storage
builder.Services.AddResourceLock();
builder.Services.AddSingleton<IResourceLockStorage, CacheResourceLockStorage>();
```

## Configuration

No additional configuration required.

## Dependencies

- `Framework.ResourceLocks.Core`
- `Framework.Caching.Abstractions`

## Side Effects

- Registers `IResourceLockStorage` as singleton
- Registers `IThrottlingResourceLockStorage` as singleton (optional)
