# Headless.Caching.Bcl

Adapter that exposes a named Headless cache as `Microsoft.Extensions.Caching.Distributed.IDistributedCache`.

## Problem Solved

Provides standard BCL distributed-cache interop for ASP.NET Core Session and third-party libraries that require `IDistributedCache`, while keeping application code on the richer Headless `ICache` API.

## Key Features

- Registers `IDistributedCache` over an internal adapter backed by a named `ICache`; consumers only ever see `IDistributedCache`.
- The adapter implements the buffer-oriented extension `IBufferDistributedCache`: callers that take its `TryGet(IBufferWriter<byte>)` / `Set(ReadOnlySequence<byte>)` members stream through the `IBufferCache` fast path with no intermediate `byte[]` when the backing cache is `Headless.Caching.Redis`/`Headless.Caching.InMemory`/`Headless.Caching.Hybrid`, and a transparent `byte[]` fallback otherwise. The remaining copy per side is the unavoidable network I/O.
- `setup.UseBclCache(...)` provisions a dedicated named cache and registers it as `IDistributedCache`.
- Maps `DistributedCacheEntryOptions` absolute, relative, and sliding expiration to `CacheEntryOptions`.
- Uses `ICache.RefreshAsync` for `IDistributedCache.Refresh`/`RefreshAsync`, so sliding entries can be re-armed without returning their value.
- Stores `byte[]` payloads in the Redis value segment unchanged rather than JSON/base64 encoded, because `byte[]` is the cache's native wire format (stored verbatim, never through a serializer); no serializer is wired.
- Supports ASP.NET Core Session round-trips when backed by a Redis named cache.

## Design Notes

This package is an interop adapter, not a general application-cache abstraction. Prefer injecting `ICache` for code you own; use `IDistributedCache` only where a framework or third-party component demands the BCL contract.

The adapter targets a dedicated named cache so BCL `byte[]` payloads stay isolated in their own namespace. `byte[]` is the cache's native wire format, stored verbatim (never through a serializer), so the adapter's `byte[]` writes land as raw bytes under any serializer — no serializer is wired, and the `configureCache` callback selects only the backing provider. The named cache still uses the normal Headless provider pipeline, so Redis entries retain the Headless frame header for logical expiration, physical expiration, sliding metadata, tags, and rolling-upgrade behavior; only the value segment is raw bytes. Because `byte[]` is native, the `IBufferDistributedCache` fast path and the typed `byte[]` path are always byte-consistent, so the buffer-oriented read/write is safe without any serializer configuration.

`DistributedCacheEntryOptions` maps to `CacheEntryOptions.Duration`, so sliding-only or option-less BCL writes use `HeadlessDistributedCacheAdapterOptions.DefaultAbsoluteExpiration` as the absolute cap. The default cap is one day. A `Set` whose absolute expiration is already in the past yields a non-positive duration, which the engine treats as "expire immediately" (an immediate eviction across every provider), matching `Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache` rather than throwing.

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
    setup.UseBclCache(
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

The `configureCache` callback passed to `UseBclCache(...)` selects only the backing provider for the named cache — exactly one, usually `instance.UseRedis(...)`. `byte[]` is the cache's native wire format, so no serializer configuration is needed.

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Caching.Core`
- `Headless.Hosting`
- `Headless.Serializer.Abstractions`
- `Microsoft.Extensions.Caching.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

## Side Effects

- Adds a named cache instance through `setup.AddNamed(...)`.
- Registers the internal adapter as singleton and `IDistributedCache` as singleton (`TryAdd`).
- Registers `HeadlessDistributedCacheAdapterOptions` with FluentValidation and startup validation.
- Registers `TimeProvider.System` when no `TimeProvider` is already registered.
