---
status: pending
priority: p1
issue_id: "061"
tags: [code-review, dotnet, rabbitmq, async, idisposable]
created: 2026-01-20
dependencies: []
---

# Broken Async Disposal Pattern

## Problem

**File:** `src/Framework.Messages.RabbitMQ/IConnectionChannelPool.cs:171-182`

```csharp
public async ValueTask DisposeAsync()
{
    Dispose();  // Calling sync Dispose from async DisposeAsync!
    await Task.CompletedTask;  // Meaningless await
}

public void Dispose()
{
    _connection?.Dispose();
    _connectionLock?.Dispose();
    _checkConnection?.Dispose();
}
```

**Issues:**
- Violates async disposal pattern
- Should dispose async-capable resources asynchronously
- RabbitMQ IConnection may benefit from async disposal

## Solution

```csharp
public async ValueTask DisposeAsync()
{
    if (_connection != null)
    {
        try
        {
            await _connection.CloseAsync().AnyContext();
        }
        catch { /* Best effort */ }

        _connection.Dispose();
    }

    _connectionLock?.Dispose();
    _checkConnection?.Dispose();

    GC.SuppressFinalize(this);
}

public void Dispose()
{
    _connection?.Dispose();
    _connectionLock?.Dispose();
    _checkConnection?.Dispose();
}
```

## Acceptance Criteria

- [ ] Fix DisposeAsync to properly dispose async resources
- [ ] Add GC.SuppressFinalize
- [ ] Keep Dispose for non-async callers
- [ ] Add test: verify async disposal completes
- [ ] Run tests

**Effort:** 1 hour | **Risk:** Low
