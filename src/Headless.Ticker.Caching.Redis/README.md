# Headless.Ticker.Caching.Redis

Redis-backed distributed coordination for TickerQ with node heartbeat monitoring and dead node detection.

## Problem Solved

Enables multi-instance TickerQ deployments with Redis-based node registry, heartbeat monitoring, and automatic dead node cleanup for high availability job scheduling.

## Key Features

- **Node Registry**: Track all TickerQ nodes in Redis
- **Heartbeat Monitoring**: Periodic node liveness checks
- **Dead Node Detection**: Automatic cleanup of failed nodes
- **Distributed Coordination**: Shared state across TickerQ instances
- **Dashboard Integration**: Real-time cluster visibility

## Installation

```bash
dotnet add package Headless.Ticker.Caching.Redis
```

## Quick Start

```csharp
builder.Services.AddTickerQ(options =>
{
    options.MaxConcurrency(10);

    // Enable Redis coordination
    options.UseRedisCoordination(redis =>
    {
        redis.ConnectionString = "localhost:6379";
        redis.NodeHeartbeatInterval = TimeSpan.FromSeconds(30);
    });
});

app.UseTickerQ();
```

## Configuration

```csharp
options.UseRedisCoordination(redis =>
{
    redis.ConnectionString = "localhost:6379,ssl=true,password=secret";
    redis.NodeHeartbeatInterval = TimeSpan.FromSeconds(30);
    redis.NodeIdentifier = "instance-1"; // Auto-generated if not set
});
```

## Dependencies

- `Headless.Ticker.Abstractions`
- `Microsoft.Extensions.Caching.StackExchangeRedis`

## Side Effects

- Stores node registry and heartbeats in Redis
- Background service sends periodic heartbeats
- Periodically scans for and removes dead nodes
- Creates Redis keys: `nodes:registry`, `hb:{nodeId}`
