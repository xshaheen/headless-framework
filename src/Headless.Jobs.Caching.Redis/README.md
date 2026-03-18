# Headless.Jobs.Caching.Redis

Redis-backed distributed coordination for Jobs with node heartbeat monitoring and dead node detection.

## Problem Solved

Enables multi-instance Jobs deployments with Redis-based node registry, heartbeat monitoring, and automatic dead node cleanup for high availability job scheduling.

## Key Features

- **Node Registry**: Track all Jobs nodes in Redis
- **Heartbeat Monitoring**: Periodic node liveness checks
- **Dead Node Detection**: Automatic cleanup of failed nodes
- **Distributed Coordination**: Shared state across Jobs instances
- **Dashboard Integration**: Real-time cluster visibility

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
        redis.NodeHeartbeatInterval = TimeSpan.FromSeconds(30);
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
    redis.NodeHeartbeatInterval = TimeSpan.FromSeconds(30);
});

builder.Services.AddHeadlessJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.NodeIdentifier = "instance-1";
    });
});
```

## Dependencies

- `Headless.Jobs.Abstractions`
- `Microsoft.Extensions.Caching.StackExchangeRedis`

## Side Effects

- Stores node registry and heartbeats in Redis
- Background service sends periodic heartbeats
- Periodically scans for and removes dead nodes
- Creates Redis keys: `nodes:registry`, `hb:{nodeId}`

## Error Handling Behavior in Clusters

When a node is detected as dead, Jobs releases orphaned locks and marks affected in-progress work as skipped with a reason.
This prevents stuck jobs and allows healthy nodes to continue processing safely.
