---
status: pending
priority: p2
issue_id: "065"
tags: [code-review, dotnet, rabbitmq, async, performance]
created: 2026-01-20
dependencies: []
---

# Blocking WaitHandle in Async Loop

## Problem

**File:** `src/Framework.Messages.RabbitMQ/RabbitMQConsumerClient.cs:89-95`

```csharp
while (true)
{
    cancellationToken.ThrowIfCancellationRequested();
    cancellationToken.WaitHandle.WaitOne(timeout);  // BLOCKING!

    if (_status == RabbitMQConsumerClientStatus.Pause)
        continue;
    ...
}
```

**Issues:**
- Blocks thread pool thread for `timeout` duration
- Should use async wait instead
- Inefficient in high-concurrency scenarios

## Solution

```csharp
while (!cancellationToken.IsCancellationRequested)
{
    if (_status == RabbitMQConsumerClientStatus.Pause)
    {
        await Task.Delay(timeout, cancellationToken).AnyContext();
        continue;
    }

    // Process messages...
}
```

Or use `await cancellationToken.WaitHandle.WaitOneAsync(timeout)` if available.

## Acceptance Criteria

- [ ] Replace WaitOne with Task.Delay
- [ ] Verify cancellation still works
- [ ] Add test: verify pause honored
- [ ] Run performance tests

**Effort:** 1 hour | **Risk:** Low
