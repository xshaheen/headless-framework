# Framework.ResourceLocks.Core

Core implementation of distributed resource locking with storage abstraction.

## Problem Solved

Provides the lock provider implementation with automatic renewal, expiration handling, and support for pluggable storage backends (cache, Redis).

## Key Features

- `ResourceLockProvider` - Full implementation of `IResourceLockProvider`
- `ThrottlingResourceLockProvider` - Rate-limited lock provider
- `DisposableResourceLock` - Auto-releasing lock handle
- Storage interfaces: `IResourceLockStorage`, `IThrottlingResourceLockStorage`
- Configurable options for timeouts and expiration

## Installation

```bash
dotnet add package Framework.ResourceLocks.Core
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddResourceLock(options =>
{
    options.DefaultTimeUntilExpires = TimeSpan.FromMinutes(20);
    options.DefaultAcquireTimeout = TimeSpan.FromSeconds(30);
});

// Add storage (cache or Redis)
builder.Services.AddResourceLockCacheStorage();
// or
builder.Services.AddResourceLockRedisStorage();
```

## Configuration

### Options

```csharp
services.AddResourceLock(options =>
{
    options.DefaultTimeUntilExpires = TimeSpan.FromMinutes(20);
    options.DefaultAcquireTimeout = TimeSpan.FromSeconds(30);
});
```

## Dependencies

- `Framework.ResourceLocks.Abstractions`

## Side Effects

- Registers `IResourceLockProvider` as singleton
- Registers `IThrottlingResourceLockProvider` as singleton (if throttling storage is provided)
