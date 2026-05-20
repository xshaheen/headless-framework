---
domain: Rate Limiting
packages: RateLimiting.Abstractions, RateLimiting.Core, RateLimiting.Cache, RateLimiting.Redis
---

# Rate Limiting

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Sliding-window Lease](#sliding-window-lease)
    - [Storage Keys and Period Boundaries](#storage-keys-and-period-boundaries)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.RateLimiting.Abstractions](#headlessratelimitingabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.RateLimiting.Core](#headlessratelimitingcore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.RateLimiting.Cache](#headlessratelimitingcache)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.RateLimiting.Redis](#headlessratelimitingredis)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)

> Distributed sliding-window rate limiting with cache and Redis storage providers.

## Quick Orientation

Use `IDistributedRateLimiter` when a resource may acquire only N leases per configured time period across a shared storage backend. The current implementation is `SlidingWindowDistributedRateLimiter`; it returns `null` when the caller cannot acquire a lease before `acquireTimeout`. Do not use distributed-lock APIs for this domain.

## Agent Instructions

- Code against `IDistributedRateLimiter` from `Headless.RateLimiting.Abstractions`.
- Use `SlidingWindowRateLimiterOptions.MaxHitsPerPeriod` and `RateLimitingPeriod` to express the limit.
- Do not call this a distributed lock in code, docs, or user-facing text; it is a rate limiter.
- Use `Headless.RateLimiting.Cache` when an existing `ICache` provider should store counters.
- Use `Headless.RateLimiting.Redis` when rate-limiter counters should use direct Redis storage and setup helpers.
- The period-boundary spin guard is intentional. Do not remove the post-delay 1ms spin without replacing the FakeTimeProvider regression test.
- A `null` result from `TryAcquireAsync(...)` means the lease was not acquired before timeout or cancellation.
- Use `IsLockedAsync(resource, ct)` when you only need to check rate-limit status without consuming a slot.
- Default acquire timeout is 30 seconds. Pass `acquireTimeout: Timeout.InfiniteTimeSpan` only when the caller must wait indefinitely; pass `TimeSpan.Zero` for a single no-wait attempt.

## Core Concepts

Rate limiting counts lease acquisitions per resource and period. A lease is a record of admission, not a lease that can be released.

### Sliding-window Lease

`SlidingWindowDistributedRateLimiter` uses a storage key built from the resource and current period start. Each acquired lease increments that period's counter. Slots free when the storage key expires.

### Storage Keys and Period Boundaries

The provider sleeps until the current period should end, then verifies that the calculated key has rotated. This protects Linux and .NET timer paths that can wake 1-4ms early. If the key does not rotate after the spin cap, the provider logs a warning and retries through the normal acquire loop.

## Choosing a Provider

Choose based on where counters should live.

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| `Headless.RateLimiting.Cache` | You already have an `ICache` provider and want rate limiting to reuse it. | The configured cache is in-memory but the app is multi-instance. | Reuses cache infrastructure but inherits cache provider semantics. |
| `Headless.RateLimiting.Redis` | You want direct Redis counters with setup helpers. | You do not operate Redis. | Requires `IConnectionMultiplexer` and Redis script loading. |

---

## Headless.RateLimiting.Abstractions

Defines public distributed rate-limiting contracts.

### Problem Solved

Lets application code depend on rate-limiting interfaces without referencing a concrete storage backend.

### Key Features

- `IDistributedRateLimiter` for acquiring leases.
- `IDistributedRateLimiterLease` with resource, acquisition time, and wait duration.
- `IDistributedRateLimiterStorage` as the public storage extension seam.
- `IsLockedAsync(resource)` to check if the resource is currently at its period limit without acquiring a lease.

### Installation

```bash
dotnet add package Headless.RateLimiting.Abstractions
```

### Quick Start

```csharp
public sealed class ImportWorker(IDistributedRateLimiter rateLimiter)
{
    public async Task RunAsync(string tenantId, CancellationToken ct)
    {
        var lease = await rateLimiter.TryAcquireAsync($"tenant:{tenantId}:import", cancellationToken: ct);

        if (lease is null)
            return;

        // continue with rate-limited work
    }
}
```

### Configuration

None.

### Dependencies

None.

### Side Effects

None.

---

## Headless.RateLimiting.Core

Provides the sliding-window implementation and setup extensions.

### Problem Solved

Implements distributed rate-limiter lease acquisition over a pluggable storage provider.

### Key Features

- `SlidingWindowDistributedRateLimiter` implements `IDistributedRateLimiter`.
- `SlidingWindowRateLimiterOptions` controls key prefix, hit limit, and period.
- `AddRateLimiter(...)` and `AddKeyedRateLimiter(...)` wire default and keyed instances.
- Period-boundary spin guard prevents stale-key retries after early timer wakes.

### Design Notes

- Rate-limiter storage is a public abstraction so provider packages can depend on Abstractions without depending on Core.
- `TryAcquireAsync(...)` returns `null` for timeout and cancellation to preserve the existing rate-limiter branch behavior.

### Installation

```bash
dotnet add package Headless.RateLimiting.Core
```

### Quick Start

```csharp
builder.Services.AddRateLimiter<MyRateLimiterStorage>(
    options =>
    {
        options.MaxHitsPerPeriod = 100;
        options.RateLimitingPeriod = TimeSpan.FromMinutes(1);
    }
);
```

### Keyed Quick Start

Register multiple named limiter configurations and resolve them via `[FromKeyedServices(...)]`:

```csharp
builder.Services.AddKeyedRateLimiter<MyRateLimiterStorage>(
    "api",
    options =>
    {
        options.MaxHitsPerPeriod = 100;
        options.RateLimitingPeriod = TimeSpan.FromMinutes(1);
    }
);

public sealed class ApiThrottle(
    [FromKeyedServices("api")] IDistributedRateLimiter limiter
)
{
    public Task<IDistributedRateLimiterLease?> TryAcquireAsync(string resource, CancellationToken ct)
        => limiter.TryAcquireAsync(resource, cancellationToken: ct);
}
```

### Configuration

```csharp
options.KeyPrefix = "rate-limiter:";
options.MaxHitsPerPeriod = 100;
options.RateLimitingPeriod = TimeSpan.FromMinutes(15);
```

### Dependencies

- `Headless.RateLimiting.Abstractions`
- `Headless.Core`
- `Headless.Hosting`

### Side Effects

- Registers `IDistributedRateLimiter` as singleton.
- Registers `TimeProvider.System` when absent.
- Registers validated `SlidingWindowRateLimiterOptions`.

---

## Headless.RateLimiting.Cache

Cache-backed storage for distributed rate limiting.

### Problem Solved

Stores sliding-window counters in any `Headless.Caching` provider.

### Key Features

- `CacheDistributedRateLimiterStorage` implements `IDistributedRateLimiterStorage`.
- Works with memory, Redis, hybrid, or custom cache providers through `ICache`.
- No setup class; registration goes through `Headless.RateLimiting.Core`.

### Installation

```bash
dotnet add package Headless.RateLimiting.Cache
```

### Quick Start

```csharp
builder.Services.AddRateLimiter<CacheDistributedRateLimiterStorage>(
    options => options.MaxHitsPerPeriod = 100
);
```

### Configuration

No storage-specific configuration. Configure the selected `ICache` provider and `SlidingWindowRateLimiterOptions`.

### Dependencies

- `Headless.Caching.Abstractions`
- `Headless.RateLimiting.Abstractions`

### Side Effects

None. The package only provides storage.

---

## Headless.RateLimiting.Redis

Redis-backed storage and setup helpers for distributed rate limiting.

### Problem Solved

Stores sliding-window counters directly in Redis for multi-instance rate limiting.

### Key Features

- `RedisDistributedRateLimiterStorage` implements `IDistributedRateLimiterStorage`.
- `AddRedisRateLimiter(...)` registers a Redis-backed default limiter.
- `AddKeyedRedisRateLimiter(...)` registers named limiter configurations.

### Installation

```bash
dotnet add package Headless.RateLimiting.Redis
```

### Quick Start

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

### Configuration

No Redis-specific options. Configure `IConnectionMultiplexer` and `SlidingWindowRateLimiterOptions`.

### Dependencies

- `Headless.RateLimiting.Abstractions`
- `Headless.RateLimiting.Core`
- `Headless.Redis`
- `StackExchange.Redis`

### Side Effects

- Registers `HeadlessRedisScriptsLoader`.
- Registers `IDistributedRateLimiter` through `Headless.RateLimiting.Core`.
