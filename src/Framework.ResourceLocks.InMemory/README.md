# Framework.ResourceLocks.InMemory

In-memory resource lock provider for single-instance deployments, development, and testing.

## Problem Solved

Provides lightweight distributed locking and throttling using in-memory storage without external dependencies. Suitable for single-instance applications, local development, and testing scenarios.

## Key Features

- **Zero Dependencies**: No Redis or database required
- **Fast**: In-process lock operations
- **Throttling Support**: Rate limiting with in-memory storage
- **Keyed Locks**: Multiple named lock instances
- **Testing**: Deterministic behavior for tests

## Installation

```bash
dotnet add package Framework.ResourceLocks.InMemory
```

## Quick Start

```csharp
// Register in-memory resource locks
builder.Services.AddInMemoryResourceLock();

// Use resource locks
public sealed class OrderService(IResourceLockProvider locks)
{
    public async Task ProcessOrderAsync(string orderId, CancellationToken ct)
    {
        await using var lockHandle = await locks.AcquireAsync(
            $"order:{orderId}",
            TimeSpan.FromSeconds(30),
            ct);

        if (lockHandle == null)
        {
            // Lock acquisition failed, order is being processed elsewhere
            return;
        }

        // Process order with exclusive lock
    }
}

// Throttling example
builder.Services.AddInMemoryThrottlingResourceLock(new ThrottlingResourceLockOptions
{
    MaxRequests = 100,
    TimeWindow = TimeSpan.FromMinutes(1)
});
```

## Configuration

```csharp
// Basic resource lock
builder.Services.AddInMemoryResourceLock((options, sp) =>
{
    options.DefaultExpiration = TimeSpan.FromMinutes(5);
});

// Throttling lock
builder.Services.AddInMemoryThrottlingResourceLock(new ThrottlingResourceLockOptions
{
    MaxRequests = 100,
    TimeWindow = TimeSpan.FromMinutes(1)
});

// Keyed throttling lock
builder.Services.AddKeyedInMemoryThrottlingResourceLock("api", new ThrottlingResourceLockOptions
{
    MaxRequests = 1000,
    TimeWindow = TimeSpan.FromMinutes(1)
});
```

## Dependencies

- `Framework.ResourceLocks.Cache`
- `Framework.Caching.Foundatio.Memory`
- `Framework.Messaging.Foundatio`

## Side Effects

None. Locks are stored in memory only and lost on restart. Not suitable for multi-instance production deployments.
