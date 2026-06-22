# Headless.Blobs.Redis

Redis implementation of the `IBlobStorage` interface for caching small blobs in Redis.

## Problem Solved

Provides high-speed blob storage for small files using Redis, suitable for temporary files, cache data, or session-related binary content.

## Key Features

- Full `IBlobStorage` implementation using Redis
- Suitable for small-to-medium sized blobs
- Fast read/write performance
- Automatic key expiration support
- Metadata stored alongside blobs

## Installation

```bash
dotnet add package Headless.Blobs.Redis
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseRedis(options =>
        options.ConnectionMultiplexer = ConnectionMultiplexer.Connect("localhost:6379")));
```

## Configuration

`RedisBlobStorageOptions` requires an `IConnectionMultiplexer` instance; the options cannot be bound from `appsettings.json` directly. Wire up the multiplexer in code as shown in Quick Start.

### Options

| Option | Default | Description |
|--------|---------|-------------|
| `ConnectionMultiplexer` | *(required)* | `IConnectionMultiplexer` instance for Redis. |
| `MaxBlobSizeBytes` | 10 MB | Maximum blob size in bytes. Set to 0 to disable. |
| `MaxBulkParallelism` | 10 | Maximum parallelism for bulk operations. |

## Usage Notes

**Size Limits:** Redis blob storage is designed for small, ephemeral blobs (cache data, session files, temporary uploads). The default 10 MB limit prevents memory exhaustion. For large files, use Azure Blob Storage or S3.

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Core`
- `Headless.Hosting`
- `StackExchange.Redis`

## Side Effects

- Registers `IBlobStorage` as singleton
- Requires an `IConnectionMultiplexer` to be provided via `RedisBlobStorageOptions.ConnectionMultiplexer`
