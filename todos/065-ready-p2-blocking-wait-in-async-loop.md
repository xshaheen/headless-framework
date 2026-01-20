---
status: ready
priority: p2
issue_id: "065"
tags: [code-review, dotnet, rabbitmq, async, performance]
created: 2026-01-20
resolved: 2026-01-21
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
}
```

**Issues:**
- Blocks thread pool thread for `timeout` duration
- Should use async wait instead
- Inefficient in high-concurrency scenarios

## Resolution

Replaced blocking `WaitOne` with async `Task.Delay`:

```csharp
while (!cancellationToken.IsCancellationRequested)
{
    await Task.Delay(timeout, cancellationToken).AnyContext();
}
```

**Changes:**
- Used `while (!cancellationToken.IsCancellationRequested)` pattern (matches Kafka implementation)
- Replaced `cancellationToken.WaitHandle.WaitOne(timeout)` with `await Task.Delay(timeout, cancellationToken).AnyContext()`
- Removed redundant `ThrowIfCancellationRequested()` call
- When cancelled, `Task.Delay` throws `TaskCanceledException` which is caught by caller

**Verification:**
- Code compiles successfully
- Pattern matches other implementations (Kafka)
- Caller handles `OperationCanceledException` correctly (IConsumerRegister.Default.cs:143)

## Acceptance Criteria

- [x] Replace WaitOne with Task.Delay
- [x] Verify cancellation still works
- [ ] Add test: verify pause honored (N/A - no pause status in RabbitMQ impl)
- [ ] Run performance tests (deferred)

**Effort:** 15 min | **Risk:** Low
