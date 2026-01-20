---
status: pending
priority: p1
issue_id: "059"
tags: [code-review, dotnet, rabbitmq, async, concurrency]
created: 2026-01-20
dependencies: []
---

# Semaphore Leak in RabbitMQ Connection Pool

## Problem

**File:** `src/Framework.Messages.RabbitMQ/IConnectionChannelPool.cs:75-92`

```csharp
public IModel Rent()
{
    while (_count > _maxSize)
    {
        Thread.SpinWait(1);  // Also a problem (busy-wait)
    }

    Interlocked.Increment(ref _count);

    var connection = GetConnection();  // Can throw!
    var channel = connection.CreateModel();  // Can throw!
    // If exception → _count never decremented!

    channel.BasicQos(0, _qosOptions.Value.PrefetchCount, false);
    return channel;
}
```

**Impact:** Semaphore leak causes permanent pool exhaustion after exceptions.

## Solution

```csharp
public IModel Rent()
{
    while (_count > _maxSize)
    {
        Thread.SpinWait(1);
    }

    Interlocked.Increment(ref _count);

    try
    {
        var connection = GetConnection();
        var channel = connection.CreateModel();
        channel.BasicQos(0, _qosOptions.Value.PrefetchCount, false);
        return channel;
    }
    catch
    {
        Interlocked.Decrement(ref _count);
        throw;
    }
}
```

## Acceptance Criteria

- [ ] Add try/catch with decrement in catch
- [ ] Add test: force GetConnection exception, verify count restored
- [ ] Add test: force CreateModel exception, verify count restored
- [ ] Verify pool doesn't exhaust after exceptions
- [ ] Run integration tests

**Effort:** 1 hour | **Risk:** Low
