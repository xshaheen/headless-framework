# Framework.Blobs.Redis

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
dotnet add package Framework.Blobs.Redis
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRedisBlobStorage(options =>
{
    options.ConnectionString = "localhost:6379";
    options.KeyPrefix = "blobs:";
});
```

## Configuration

### appsettings.json

```json
{
  "RedisBlob": {
    "ConnectionString": "localhost:6379,password=secret",
    "KeyPrefix": "blobs:"
  }
}
```

### Options

| Option | Default | Description |
|--------|---------|-------------|
| `MaxBlobSizeBytes` | 10 MB | Maximum blob size. Set to 0 to disable. |
| `MaxBulkParallelism` | 10 | Maximum parallelism for bulk operations. |

## Usage Notes

**Size Limits:** Redis blob storage is designed for small, ephemeral blobs (cache data, session files, temporary uploads). The default 10 MB limit prevents memory exhaustion. For large files, use Azure Blob Storage or S3.

## Dependencies

- `Framework.Blobs.Abstractions`
- `Framework.BuildingBlocks`
- `Framework.Hosting`
- `StackExchange.Redis`

## Side Effects

- Registers `IBlobStorage` as singleton
- Requires Redis connection (uses existing `IConnectionMultiplexer` if registered)
