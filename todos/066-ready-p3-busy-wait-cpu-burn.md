---
status: ready
priority: p3
issue_id: "066"
tags: [code-review, performance, rabbitmq, optimization]
created: 2026-01-20
resolved: 2026-01-21
dependencies: []
---

# Busy-Wait Spin Lock Burns CPU - RESOLVED

## Problem

**File:** `src/Framework.Messages.RabbitMQ/IConnectionChannelPool.cs:75-78`

```csharp
while (_count > _maxSize)
{
    Thread.SpinWait(1);  // CPU-burning busy-wait
}
```

**Impact:**
- Burns CPU cycles while waiting
- Should use proper async wait mechanism
- Not scalable under load

## Solution

**Option 1: SemaphoreSlim** (Recommended)
```csharp
private readonly SemaphoreSlim _poolSemaphore;

public IConnectionChannelPool(...)
{
    _poolSemaphore = new SemaphoreSlim(_maxSize, _maxSize);
}

public async Task<IModel> RentAsync()
{
    await _poolSemaphore.WaitAsync().AnyContext();

    try
    {
        var connection = await GetConnectionAsync().AnyContext();
        var channel = connection.CreateModel();
        channel.BasicQos(0, _qosOptions.Value.PrefetchCount, false);
        return channel;
    }
    catch
    {
        _poolSemaphore.Release();
        throw;
    }
}

public void Return(IModel channel)
{
    try
    {
        channel?.Close();
        channel?.Dispose();
    }
    finally
    {
        _poolSemaphore.Release();
    }
}
```

**LOC savings:** Removes Interlocked counter, simplifies logic.

## Acceptance Criteria

- [x] Replace busy-wait with SemaphoreSlim
- [x] Make Rent async (already was async)
- [x] Update callers (RabbitMQTransport already uses await)
- [x] Dispose semaphore in Dispose method
- [x] Existing tests pass

**Effort:** 3 hours | **Risk:** Medium

## Resolution Summary

Replaced the CPU-burning busy-wait spin lock with SemaphoreSlim for proper async pool limiting:

**Changes made:**
- Added `SemaphoreSlim _poolSemaphore` field initialized to pool size (15)
- Updated `IConnectionChannelPool.Rent()` to use `await _poolSemaphore.WaitAsync()` with release on exception
- Updated `IConnectionChannelPool.Return()` to release semaphore in finally block
- Added semaphore disposal in `Dispose()` method
- Fixed unrelated constructor naming bug: `RabbitMqTransport` → `RabbitMQTransport`

**Benefits:**
- No more CPU burning while waiting for pool capacity
- Proper async/await pattern throughout
- Thread-safe pool limiting with kernel-level synchronization
- Scalable under high load

**Files modified:**
- `/Users/xshaheen/Dev/framework/headless-framework/src/Framework.Messages.RabbitMQ/IConnectionChannelPool.cs`
- `/Users/xshaheen/Dev/framework/headless-framework/src/Framework.Messages.RabbitMQ/RabbitMqTransport.cs` (constructor naming fix)
