---
status: pending
priority: p1
issue_id: "052"
tags: ["code-review","concurrency"]
dependencies: []
---

# ReportSuccess uses sync Timer.Dispose — TOCTOU race with timer callback

## Problem Statement

CircuitBreakerStateManager.ReportSuccess (line 273) calls `closedTimerToDispose?.Dispose()` synchronously. Timer.Dispose() without a WaitHandle returns immediately but does NOT guarantee the callback has finished executing. If _OnOpenTimerElapsed fires concurrently, the state guard inside the lock prevents double-transition, but the callback can still schedule a Task.Run for the resume callback AFTER the circuit was already closed. By contrast, ReportFailureAsync (line 179) and ResetAsync (line 437) both use `await timerToDispose.DisposeAsync()` which blocks until the callback completes. This inconsistency leaves a race window in ReportSuccess.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:273
- **Contrast with:** ReportFailureAsync:179, ResetAsync:437 — both use DisposeAsync correctly
- **Discovered by:** strict-dotnet-reviewer (P1.1, P2.4)

## Proposed Solutions

### Change ReportSuccess to ValueTask ReportSuccessAsync
- **Pros**: Allows awaiting DisposeAsync, closes race entirely
- **Cons**: Interface signature change
- **Effort**: Medium
- **Risk**: Low

### Pass a WaitHandle to Timer.Dispose for synchronous callback drain
- **Pros**: No interface change
- **Cons**: WaitHandle approach is deprecated and heavyweight
- **Effort**: Small
- **Risk**: Medium


## Recommended Action

Prefer changing to ValueTask ReportSuccessAsync — aligns with the rest of the API surface which is already async.

## Acceptance Criteria

- [ ] ReportSuccess/Async uses same async disposal path as ReportFailureAsync and ResetAsync
- [ ] No TOCTOU window between timer callback scheduling and circuit state read
- [ ] All call sites updated
- [ ] Test added for concurrent ReportSuccess + timer callback scenario

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
