# Headless.RateLimiting.Abstractions

Defines distributed rate-limiting contracts.

## Problem Solved

Provides a storage-agnostic API for distributed sliding-window rate limiting.

## Key Features

- `IDistributedRateLimiter` - Acquires leases for rate-limited resources.
- `IDistributedRateLimiterLease` - Describes the acquired lease.
- `IDistributedRateLimiterStorage` - Extension seam for cache and Redis storage providers.

## Installation

```bash
dotnet add package Headless.RateLimiting.Abstractions
```

## Quick Start

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

## Configuration

No configuration required. This is an abstractions package.

## Dependencies

None.

## Side Effects

None.
