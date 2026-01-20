---
status: pending
priority: p2
issue_id: "052"
tags: [code-review, dotnet, aws-sqs, async, error-handling]
created: 2026-01-20
dependencies: [049]
---

# Fire-and-Forget Task Loses Exceptions

## Problem

**File:** `src/Framework.Messages.AwsSqs/AmazonSqsConsumerClient.cs:78`

```csharp
_ = Task.Run(consumeAsync, cancellationToken).ConfigureAwait(false);
```

Discarded task means exceptions silently swallowed. If `consumeAsync()` throws:
- No logging
- Message not acknowledged
- Semaphore may not release (see #049)
- Returns to queue without visibility

## Solution

```csharp
_ = Task.Run(async () =>
{
    try
    {
        await consumeAsync().AnyContext();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error consuming message for group {GroupId}", groupId);
        _semaphore.Release();

        try
        {
            await RejectAsync(response.Messages[0].ReceiptHandle).AnyContext();
        }
        catch { /* Already logged */ }
    }
}, cancellationToken);
```

**Or** track tasks in collection and await periodically.

## Acceptance Criteria

- [ ] Add exception handling wrapper to fire-and-forget task
- [ ] Ensure semaphore released on exception
- [ ] Log unhandled exceptions
- [ ] Add test: force exception in consumeAsync, verify logged
- [ ] Verify message rejected on failure

**Effort:** 2 hours | **Risk:** Low
