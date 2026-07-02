# Headless.Blobs.Redis

Redis implementation of `IBlobStorage` for storing small, ephemeral blobs in Redis.

## Problem Solved

Provides high-speed blob storage for small files using Redis, for temporary files, cache data, or session-related binary content. Not a general-purpose store — the 10 MB default limit and Redis memory model make it unsuitable for large files.

## Key Features

- Full `IBlobStorage` implementation using Redis, routed through the shared resolve seam.
- Automatic key expiration support.
- Metadata stored in a separate info hash alongside blobs (atomic via Lua); returned by `OpenReadStreamAsync` and `GetBlobInfoAsync`.
- `HSCAN`-cursor paging: `ListAsync` wraps the native cursor in the shared opaque envelope as the token.
- Container lifecycle via `RedisBlobContainerManager` resolved from DI (`EnsureContainerAsync` is a no-op; Redis has no container concept).

## Design Notes

- Designed for small, ephemeral blobs (cache data, session files, temporary uploads). The default `MaxBlobSizeBytes` is 10 MB to prevent memory exhaustion; uploads above the cap are rejected, and non-seekable streams are buffered to memory under the same cap. For large files, use Azure Blob Storage or S3.
- **`HSCAN`-cursor paging tier.** `ListAsync` wraps the native `HSCAN` cursor in the shared opaque envelope as the continuation token, and rejects a decoded-but-non-numeric cursor with the same `ArgumentException` as a malformed envelope (HSCAN cursors are unsigned integers, so a foreign token is provably invalid here). The order is non-lexicographic and the same blob may surface more than once across a rehash — callers iterating to completion must tolerate duplicates. An ordered (sort-based) token is a deferred follow-up if a consumer needs stable order.
- **No real container.** `EnsureContainerAsync` is a no-op, `ContainerExistsAsync` is true when any key exists under the container prefix, and `DeleteContainerAsync` clears the prefix's keys.

## Installation

```bash
dotnet add package Headless.Blobs.Redis
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// The IConnectionMultiplexer must be set in code — it cannot be bound from appsettings.json.
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseRedis(options => options.ConnectionMultiplexer = ConnectionMultiplexer.Connect("localhost:6379"))
);
```

## Configuration

`RedisBlobStorageOptions` requires an `IConnectionMultiplexer` instance. The `UseRedis(IConfiguration)` overload cannot bind this interface property — options validation fails at startup if `ConnectionMultiplexer` is not set via the `Action<RedisBlobStorageOptions>` overload.

| Option | Default | Description |
|--------|---------|-------------|
| `ConnectionMultiplexer` | *(required)* | `IConnectionMultiplexer` instance for Redis. |
| `MaxBlobSizeBytes` | 10 MB | Maximum blob size in bytes. Set to `0` to disable the limit. |
| `MaxBulkParallelism` | 10 | Maximum parallelism for bulk operations. |

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Blobs.Core`
- `Headless.Core`
- `Headless.Hosting`
- `Polly.Core`
- `StackExchange.Redis`

## Side Effects

Registered via `AddHeadlessBlobs(b => b.UseRedis(...))` or `AddNamed("name", i => i.UseRedis(...))`:

- Default (`UseRedis`): registers `IBlobStorage` as unkeyed singleton and `IBlobContainerManager` as unkeyed singleton (`RedisBlobContainerManager`); registers `TimeProvider`, `IJsonOptionsProvider`, and `IJsonSerializer` as singletons (each via `TryAdd`, so existing registrations are kept).
- Named (`AddNamed ... UseRedis`): registers `IBlobStorage` and `IBlobContainerManager` each as keyed singleton (`name`); same `TryAdd` registrations for shared services.
- No presigned URL support — `IPresignedUrlBlobStorage` is never registered for Redis stores.
