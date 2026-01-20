---
status: pending
priority: p1
issue_id: "056"
tags: [code-review, dotnet, rabbitmq, async, deadlock]
created: 2026-01-20
dependencies: []
---

# Sync-over-Async Deadlock in RabbitMQ Connection Pool

## Problem

**File:** `src/Framework.Messages.RabbitMQ/IConnectionChannelPool.cs:103`

```csharp
_connection = _connectionActivator().GetAwaiter().GetResult();
```

**Critical deadlock risk:**
- Blocks thread pool thread waiting for async operation
- In ASP.NET context → instant deadlock
- SynchronizationContext captures can deadlock
- Violates async all-the-way principle

**Impact:** Production outage on first connection attempt in web apps.

## Solution

**Option 1: Make GetConnection async** (Recommended)
```csharp
private async Task<IConnection> GetConnectionAsync()
{
    if (_connection is { IsOpen: true })
        return _connection;

    _checkConnection.WaitOne(TimeSpan.FromSeconds(60));
    if (_connection is { IsOpen: true })
        return _connection;

    _connectionLock.Wait();
    try
    {
        if (_connection is { IsOpen: true })
            return _connection;

        _connection = await _connectionActivator().AnyContext();
        return _connection;
    }
    finally
    {
        _connectionLock.Release();
    }
}
```

Update callers: `Rent()` → `RentAsync()`, propagate up.

**Option 2: Lazy initialization**
Initialize connection in constructor/startup, not on-demand.

## Acceptance Criteria

- [ ] Remove `.GetAwaiter().GetResult()`
- [ ] Make GetConnection async
- [ ] Update all callers to async
- [ ] Add test: verify no deadlock in sync context
- [ ] Verify builds and tests pass

**Effort:** 3 hours | **Risk:** Medium
