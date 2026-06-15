# Headless.Caching.Bcl

Adapter that exposes a named Headless cache as `Microsoft.Extensions.Caching.Distributed.IDistributedCache`.

## Problem Solved

Provides standard BCL distributed-cache interop for ASP.NET Core Session and third-party libraries that require `IDistributedCache`, while keeping application code on the richer Headless `ICache` API.

## Key Features

- `HeadlessDistributedCacheAdapter` implements `IDistributedCache` over a named `ICache`.
- `setup.AddHeadlessDistributedCache(...)` provisions a dedicated named cache and registers it as `IDistributedCache`.
- Maps `DistributedCacheEntryOptions` absolute, relative, and sliding expiration to `CacheEntryOptions`.
- Uses `ICache.RefreshAsync` for `IDistributedCache.Refresh`/`RefreshAsync`, so sliding entries can be re-armed without returning their value.
- Wires the named cache with `RawBytesSerializer`, so `byte[]` payloads are stored in the Redis value segment unchanged rather than JSON/base64 encoded.
- Supports ASP.NET Core Session round-trips when backed by a Redis named cache.

## Design Notes

This package is an interop adapter, not a general application-cache abstraction. Prefer injecting `ICache` for code you own; use `IDistributedCache` only where a framework or third-party component demands the BCL contract.

The adapter always targets a dedicated named cache because `RawBytesSerializer` is byte-array-only by design. Mixing that serializer into the application's default cache would make typed cache values fail. The named cache still uses the normal Headless provider pipeline, so Redis entries retain the Headless frame header for logical expiration, physical expiration, sliding metadata, tags, and rolling-upgrade behavior; only the value segment is raw bytes.

`DistributedCacheEntryOptions` must always become a positive `CacheEntryOptions.Duration`, so sliding-only or option-less BCL writes use `HeadlessDistributedCacheAdapterOptions.DefaultAbsoluteExpiration` as the absolute cap. The default cap is one day. A past absolute expiration or non-positive relative/sliding duration is rejected at the adapter boundary.

The sync BCL methods block on the async implementation with `GetAwaiter().GetResult()`, matching the Microsoft Redis adapter. Prefer the async BCL methods in ASP.NET Core code paths.

## Installation

```bash
dotnet add package Headless.Caching.Bcl
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);
var redis = ConnectionMultiplexer.Connect("localhost:6379");

builder.Services.AddHeadlessCaching(setup =>
{
    setup.UseRedis(options => options.ConnectionMultiplexer = redis);
    setup.AddHeadlessDistributedCache(
        options =>
        {
            options.CacheName = "aspnet-session";
            options.DefaultAbsoluteExpiration = TimeSpan.FromHours(8);
        },
        instance =>
            instance.UseRedis(options =>
            {
                options.ConnectionMultiplexer = redis;
                options.KeyPrefix = "session:";
            })
    );
});

builder.Services.AddSession(options => options.IdleTimeout = TimeSpan.FromMinutes(20));
```

Consumers that need the standard contract can inject `IDistributedCache`; application services should still inject `ICache`.

## Configuration

| Option | Default | Description |
| --- | --- | --- |
| `CacheName` | `"bcl-distributed-cache"` | Named cache instance used by the adapter. Must be non-empty and must not be a reserved Headless cache provider key or under the reserved `Headless.Caching:` namespace. |
| `DefaultAbsoluteExpiration` | `1 day` | Absolute lifetime cap used when BCL callers provide only sliding expiration or no expiration options. Must be greater than zero. |

The `configureCache` callback passed to `AddHeadlessDistributedCache(...)` must select exactly one named cache provider, usually `instance.UseRedis(...)`. Do not configure another serializer in that callback; this adapter owns the raw-byte serializer for the named cache.

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`
- `Headless.Serializer.Abstractions`
- `Microsoft.Extensions.Caching.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

## Side Effects

- Adds a named cache instance through `setup.AddNamed(...)`, configured with `RawBytesSerializer`.
- Registers `HeadlessDistributedCacheAdapter` as singleton and `IDistributedCache` as singleton (`TryAdd`).
- Registers `HeadlessDistributedCacheAdapterOptions` with FluentValidation and startup validation.
- Registers `TimeProvider.System` when no `TimeProvider` is already registered.
