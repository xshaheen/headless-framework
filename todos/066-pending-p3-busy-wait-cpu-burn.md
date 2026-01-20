---
status: pending
priority: p3
issue_id: "066"
tags: [code-review, performance, rabbitmq, optimization]
created: 2026-01-20
dependencies: []
---

# Busy-Wait Spin Lock Burns CPU

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

- [ ] Replace busy-wait with SemaphoreSlim
- [ ] Make Rent async
- [ ] Update callers
- [ ] Add test: verify pool limits honored
- [ ] Run performance tests

**Effort:** 3 hours | **Risk:** Medium
