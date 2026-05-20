# Headless.RateLimiting.Core

Distributed sliding-window rate limiter.

## Problem Solved

Limits how many leases a resource can acquire within a configured period across a shared storage provider.

## Key Features

- `SlidingWindowDistributedRateLimiter` implements `IDistributedRateLimiter`.
- `SlidingWindowRateLimiterOptions` configures key prefix, hit limit, and period.
- Registration helpers for default and keyed rate limiters.
- Period-boundary guard avoids retrying against a stale storage key when timers wake early.

## Design Notes

- Rate-limiter storage is a public abstraction so provider packages can depend on Abstractions without depending on Core.
- `TryAcquireAsync(...)` returns `null` for timeout and cancellation to preserve the existing rate-limiter branch behavior.

## Installation

```bash
dotnet add package Headless.RateLimiting.Core
```

## Quick Start

```csharp
builder.Services.AddRateLimiter<MyRateLimiterStorage>(
    options =>
    {
        options.MaxHitsPerPeriod = 100;
        options.RateLimitingPeriod = TimeSpan.FromMinutes(1);
    }
);
```

## Configuration

```csharp
options.KeyPrefix = "rate-limiter:";
options.MaxHitsPerPeriod = 100;
options.RateLimitingPeriod = TimeSpan.FromMinutes(15);
```

## Dependencies

- `Headless.RateLimiting.Abstractions`
- `Headless.Core`
- `Headless.Hosting`

## Side Effects

- Registers `IDistributedRateLimiter` as singleton.
- Registers `TimeProvider.System` when no `TimeProvider` is already registered.
- Registers validated `SlidingWindowRateLimiterOptions`.
