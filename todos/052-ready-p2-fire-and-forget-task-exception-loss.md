---
status: ready
priority: p2
issue_id: "052"
tags: [code-review, dotnet, aws-sqs, async, error-handling]
created: 2026-01-20
dependencies: [049]
resolved: 2026-01-21
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

Added exception handling wrapper to fire-and-forget task:

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
        catch (Exception rejectEx)
        {
            _logger.LogError(rejectEx, "Failed to reject message after consume error for group {GroupId}", groupId);
        }
    }
}, cancellationToken).ConfigureAwait(false);
```

## Changes Made

1. **AmazonSqsConsumerClient.cs**: Added exception handling wrapper around fire-and-forget task
2. **AmazonSqsConsumerClientFactory.cs**: Injected ILogger<AmazonSqsConsumerClient>
3. **AmazonSqsConsumerClientTests.cs**: Added comprehensive tests

## Acceptance Criteria

- [x] Add exception handling wrapper to fire-and-forget task
- [x] Ensure semaphore released on exception
- [x] Log unhandled exceptions
- [x] Add test: force exception in consumeAsync, verify logged
- [x] Verify message rejected on failure

**Effort:** 2 hours | **Risk:** Low
