---
status: pending
priority: p1
issue_id: "049"
tags: [code-review, dotnet, aws-sqs, resource-leak, deadlock]
created: 2026-01-20
dependencies: []
---

# Semaphore Not Released in Exception Paths

## Problem Statement

**File:** `src/Framework.Messages.AwsSqs/AmazonSqsConsumerClient.cs:112-136`

`CommitAsync` and `RejectAsync` release semaphore only on success. Exceptions prevent release, causing permanent deadlock after `groupConcurrent` failures. No more messages processed.

**Critical Risk:** Production consumer permanently deadlocked.

## Findings

```csharp
// Lines 112-123 - CommitAsync
try {
    await _sqsClient!.DeleteMessageAsync(...);
    _semaphore.Release();  // Only on success!
}
catch (ReceiptHandleIsInvalidException ex) {
    _InvalidIdFormatLog(ex.Message);  // Semaphore NOT released!
}
```

Same issue in `RejectAsync` (lines 125-136).

**Data Loss Scenario:**
1. `groupConcurrent = 5`
2. 5 messages fail to commit (invalid receipt handles)
3. Semaphore count = 0
4. All subsequent messages queued but never processed
5. Queue pileup → message expiry

## Solution

```csharp
public async ValueTask CommitAsync(object? sender)
{
    if (sender is not string receiptHandle)
    {
        _semaphore.Release();
        return;
    }

    try
    {
        await _sqsClient!.DeleteMessageAsync(_queueUrl, receiptHandle).AnyContext();
    }
    catch (ReceiptHandleIsInvalidException ex)
    {
        _InvalidIdFormatLog(ex.Message);
    }
    finally
    {
        _semaphore.Release();  // ALWAYS release
    }
}
```

Apply same fix to `RejectAsync`.

## Acceptance Criteria

- [ ] Add `finally` block with semaphore release to `CommitAsync`
- [ ] Add `finally` block with semaphore release to `RejectAsync`
- [ ] Add integration test: force exception, verify semaphore released
- [ ] Stress test: 1000 messages with 10% failure rate
- [ ] Verify consumer continues processing after exceptions

**Effort:** 30 min | **Risk:** Very Low
