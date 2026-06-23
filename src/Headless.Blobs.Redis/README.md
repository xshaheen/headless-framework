# Headless.Blobs.Redis

Redis implementation of `IBlobStorage` for storing small, ephemeral blobs in Redis.

## Problem Solved

Provides high-speed blob storage for small files using Redis, for temporary files, cache data, or session-related binary content. Not a general-purpose store — the 10 MB default limit and Redis memory model make it unsuitable for large files.

## Key Features

- Full `IBlobStorage` implementation using Redis.
- Automatic key expiration support.
- Metadata stored alongside blobs in Redis.
- Fast read/write performance.

## Installation

```bash
dotnet add package Headless.Blobs.Redis
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// The IConnectionMultiplexer must be set in code — it cannot be bound from appsettings.json.
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseRedis(options =>
        options.ConnectionMultiplexer = ConnectionMultiplexer.Connect("localhost:6379")));
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
- `Headless.Core`
- `Headless.Hosting`
- `StackExchange.Redis`

## Side Effects

Registered via `AddHeadlessBlobs(b => b.UseRedis(...))` or `AddNamed("name", i => i.UseRedis(...))`:

- Default (`UseRedis`): registers `IBlobStorage` as unkeyed singleton; registers `TimeProvider`, `IJsonOptionsProvider`, and `IJsonSerializer` as singletons (each via `TryAdd`, so existing registrations are kept).
- Named (`AddNamed ... UseRedis`): registers `IBlobStorage` as keyed singleton (`name`); same `TryAdd` registrations for shared services.
- No presigned URL support — `IPresignedUrlBlobStorage` is never registered for Redis stores.
