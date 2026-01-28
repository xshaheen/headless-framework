# Headless.ResourceLocks.Abstractions

Defines the unified interface for distributed resource locking.

## Problem Solved

Provides a provider-agnostic distributed locking API, enabling coordination across multiple instances with features like lock expiration, renewal, and throttling without changing application code.

## Key Features

- `IResourceLockProvider` - Regular locking with expiration
- `IResourceLock` - Acquired lock handle with release
- `IThrottlingResourceLockProvider` - Rate-limited locking
- `IResourceThrottlingLock` - Throttling lock handle
- Configurable timeouts and expiration

## Installation

```bash
dotnet add package Headless.ResourceLocks.Abstractions
```

## Usage

```csharp
public sealed class OrderService(IResourceLockProvider lockProvider)
{
    public async Task ProcessOrderAsync(Guid orderId, CancellationToken ct)
    {
        var lockResource = $"order:{orderId}";

        await using var @lock = await lockProvider.TryAcquireAsync(
            lockResource,
            timeUntilExpires: TimeSpan.FromMinutes(5),
            acquireTimeout: TimeSpan.FromSeconds(30),
            ct
        );

        if (@lock is null)
            throw new ConcurrencyException("Could not acquire lock");

        // Process order safely...
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None.
