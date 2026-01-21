---
status: done
priority: p1
issue_id: "056"
tags: [code-review, dotnet, rabbitmq, async, deadlock]
created: 2026-01-20
completed: 2026-01-21
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

- [x] Remove `.GetAwaiter().GetResult()`
- [x] Make GetConnection async
- [x] Update all callers to async
- [x] Verify builds and tests pass

**Effort:** 1 hour | **Risk:** Low

## Resolution

**Changes made:**

1. **IConnectionChannelPool.cs** - Converted synchronous `GetConnection()` to async:
   - Changed method signature: `IConnection GetConnection()` → `Task<IConnection> GetConnectionAsync()`
   - Replaced `Lock` with `SemaphoreSlim _connectionLock` for async compatibility
   - Implemented proper async/await pattern with double-check locking
   - Used `.AnyContext()` for library code (framework convention)
   - Added `_connectionLock.Dispose()` in Dispose method
   - Linter auto-added `IAsyncDisposable` implementation

2. **RabbitMqConsumerClient.cs** - Updated caller:
   - Changed `var connection = _connectionChannelPool.GetConnection()` to `var connection = await _connectionChannelPool.GetConnectionAsync().AnyContext()`

3. **Test files** - Updated mock setups:
   - `RabbitMqConsumerClientTests.cs`: Changed mock from `GetConnection()` to `GetConnectionAsync()`
   - `RabbitMqConsumerClientValidationTests.cs`: Updated 2 mock setups to use async method

**Result:** Eliminated sync-over-async deadlock. Connection initialization now async end-to-end. Build passes successfully.
