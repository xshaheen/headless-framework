# Headless.DistributedLocks.Core

Core implementation of distributed resource locking with storage abstraction.

## Problem Solved

Provides the lock provider implementation with automatic renewal, expiration handling, and support for pluggable storage backends (cache, Redis).

## Key Features

- `DistributedLockProvider` - Full implementation of `IDistributedLockProvider`
- `ThrottlingDistributedLockProvider` - Rate-limited lock provider
- `DisposableDistributedLock` - Auto-releasing lock handle
- Storage interfaces: `IDistributedLockStorage`, `IThrottlingDistributedLockStorage`
- Configurable options for timeouts and expiration

## Installation

```bash
dotnet add package Headless.DistributedLocks.Core
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDistributedLock(options =>
{
    options.DefaultTimeUntilExpires = TimeSpan.FromMinutes(20);
    options.DefaultAcquireTimeout = TimeSpan.FromSeconds(30);
});

// Add storage (cache or Redis)
builder.Services.AddDistributedLockCacheStorage();
// or
builder.Services.AddDistributedLockRedisStorage();
```

## Configuration

### Options

```csharp
services.AddDistributedLock(options =>
{
    options.DefaultTimeUntilExpires = TimeSpan.FromMinutes(20);
    options.DefaultAcquireTimeout = TimeSpan.FromSeconds(30);
});
```

## Dependencies

- `Headless.DistributedLocks.Abstractions`

## Side Effects

- Registers `IDistributedLockProvider` as singleton
- Registers `IThrottlingDistributedLockProvider` as singleton (if throttling storage is provided)
