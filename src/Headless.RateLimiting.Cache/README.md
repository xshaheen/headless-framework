# Headless.RateLimiting.Cache

Cache-backed storage for distributed rate limiting.

## Problem Solved

Stores sliding-window rate-limiter counters in any `Headless.Caching` provider.

## Key Features

- `CacheDistributedRateLimiterStorage` implements `IDistributedRateLimiterStorage`.
- Works with memory, Redis, hybrid, or custom cache providers through `ICache`.
- No setup class; register through `Headless.RateLimiting.Core`.

## Installation

```bash
dotnet add package Headless.RateLimiting.Cache
```

## Quick Start

```csharp
builder.Services.AddRateLimiter<CacheDistributedRateLimiterStorage>(
    options => options.MaxHitsPerPeriod = 100
);
```

## Configuration

Configuration is owned by `SlidingWindowRateLimiterOptions` in `Headless.RateLimiting.Core`.

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.RateLimiting.Abstractions`

## Side Effects

None. The package only provides storage.
