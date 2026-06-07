# Headless.Jobs.Caching.Redis

Redis-backed cron-expression caching for Jobs in multi-instance deployments.

## Problem Solved

Caches cron-expression lookups in Redis so multi-instance Jobs deployments avoid repeated database reads for the cron-expression set. This package is caching only -- node liveness, heartbeat, and membership are provided by `Headless.Coordination`, not Redis.

## Key Features

- **Cron-Expression Caching**: Shared Redis cache for the cron-expression set across instances
- **Distributed Cache Access**: Exposes the underlying distributed cache to the EF persistence layer
- **Connection Awareness**: Skips cache invalidation when no Redis connection is available

## Installation

```bash
dotnet add package Headless.Jobs.Caching.Redis
```

## Quick Start

```csharp
builder.Services
    .AddHeadlessJobs(options =>
    {
        options.ConfigureScheduler(scheduler => scheduler.MaxConcurrency = 10);
    })
    .AddStackExchangeRedis(redis =>
    {
        redis.Configuration = "localhost:6379";
    });
```

## Configuration

```csharp
builder.Services
    .AddHeadlessJobs()
    .AddStackExchangeRedis(redis =>
    {
        redis.Configuration = "localhost:6379,ssl=true,password=secret";
        redis.InstanceName = "jobs:";
    });
```

`AddStackExchangeRedis` takes `JobsRedisOptionBuilder` (a `RedisCacheOptions`). There is no node-heartbeat option -- membership/liveness is configured through `AddHeadlessCoordination(...)`.

## Dependencies

- `Headless.Jobs.Abstractions`
- `Microsoft.Extensions.Caching.StackExchangeRedis`

## Side Effects

- Registers `IJobsCacheContext` backed by Redis for cron-expression caching
- Stores cron-expression cache entries under the configured `InstanceName` prefix
