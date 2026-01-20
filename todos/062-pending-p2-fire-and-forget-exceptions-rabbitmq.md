---
status: pending
priority: p2
issue_id: "062"
tags: [code-review, dotnet, rabbitmq, async, error-handling]
created: 2026-01-20
dependencies: []
---

# Fire-and-Forget Task Loses Exceptions

## Problem

**File:** `src/Framework.Messages.RabbitMQ/RabbitMQBasicConsumer.cs:35-44`

```csharp
_ = Task.Run(() => _Consume(ea, cancellationToken), cancellationToken).ConfigureAwait(false);
```

Discarded task = exceptions silently swallowed. If `_Consume()` throws:
- No logging
- Message not acknowledged
- Consumer may stop processing

## Solution

```csharp
_ = Task.Run(async () =>
{
    try
    {
        await _Consume(ea, cancellationToken).AnyContext();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error consuming RabbitMQ message");

        try
        {
            _channel.BasicNack(ea.DeliveryTag, false, requeue: true);
        }
        catch { /* Already logged */ }
    }
}, cancellationToken);
```

## Acceptance Criteria

- [ ] Add exception handling wrapper to Task.Run
- [ ] Log unhandled exceptions
- [ ] Nack message on exception
- [ ] Add test: force exception in _Consume, verify logged
- [ ] Verify message requeued on failure

**Effort:** 2 hours | **Risk:** Low
