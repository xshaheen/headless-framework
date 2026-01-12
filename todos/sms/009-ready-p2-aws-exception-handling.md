---
status: ready
priority: p2
issue_id: "009"
tags: [code-review, aws, sms, error-handling]
dependencies: []
---

# AwsSnsSmsSender catches all exceptions including OperationCanceledException

## Problem Statement

`AwsSnsSmsSender.SendAsync` has a broad `catch (Exception e)` that catches `OperationCanceledException`, treating cancellation as a failure instead of respecting cancellation semantics.

## Findings

- **File:** `src/Framework.Sms.Aws/AwsSnsSmsSender.cs:78-83`
- **Current code:**
```csharp
catch (Exception e)
{
    logger.LogError(e, "Failed to send using AWS SMS {@Request}", publishRequest);
    return SendSingleSmsResponse.Failed(e.Message);
}
```
- When operation is cancelled, this logs an error and returns failure
- Cancellation should be re-thrown to respect caller's intent
- Similar issue in other providers with try-catch

## Proposed Solutions

### Option 1: Handle OperationCanceledException separately

**Approach:** Catch and rethrow `OperationCanceledException` before the generic catch.

```csharp
try
{
    // ...
}
catch (OperationCanceledException)
{
    throw;  // Respect cancellation
}
catch (Exception e)
{
    logger.LogError(e, "Failed to send using AWS SMS {@Request}", publishRequest);
    return SendSingleSmsResponse.Failed(e.Message);
}
```

**Pros:**
- Proper cancellation semantics
- Caller can distinguish between failure and cancellation

**Cons:**
- None

**Effort:** 15 minutes

**Risk:** Low

## Recommended Action

Implement Option 1 - catch and rethrow `OperationCanceledException`.

## Technical Details

**Affected files:**
- `src/Framework.Sms.Aws/AwsSnsSmsSender.cs:78-83`
- `src/Framework.Sms.Infobip/InfobipSmsSender.cs:65-71` (similar issue)

## Acceptance Criteria

- [ ] OperationCanceledException is rethrown, not caught as failure
- [ ] Cancellation is not logged as error
- [ ] Other exceptions still handled and logged

## Work Log

### 2026-01-12 - Code Review Discovery

**By:** Claude Code

**Actions:**
- Identified broad exception catch pattern
- Confirmed OperationCanceledException should be rethrown
