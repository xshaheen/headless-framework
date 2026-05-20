# Headless.RateLimiting.Redis

Redis-backed storage and setup helpers for distributed rate limiting.

## Problem Solved

Stores sliding-window counters in Redis for multi-instance rate limiting.

## Key Features

- `RedisDistributedRateLimiterStorage` implements `IDistributedRateLimiterStorage`.
- `AddRedisRateLimiter(...)` registers a default Redis-backed limiter.
- `AddKeyedRedisRateLimiter(...)` registers named limiter configurations.

## Installation

```bash
dotnet add package Headless.RateLimiting.Redis
```

## Quick Start

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(
    _ => ConnectionMultiplexer.Connect("localhost:6379")
);

builder.Services.AddRedisRateLimiter(options =>
{
    options.MaxHitsPerPeriod = 100;
    options.RateLimitingPeriod = TimeSpan.FromMinutes(1);
});
```

## Configuration

Configuration is owned by `SlidingWindowRateLimiterOptions` in `Headless.RateLimiting.Core`.

## Dependencies

- `Headless.RateLimiting.Abstractions`
- `Headless.RateLimiting.Core`
- `Headless.Redis`
- `StackExchange.Redis`

## Side Effects

- Registers `HeadlessRedisScriptsLoader`.
- Registers `IDistributedRateLimiter` through `Headless.RateLimiting.Core`.
