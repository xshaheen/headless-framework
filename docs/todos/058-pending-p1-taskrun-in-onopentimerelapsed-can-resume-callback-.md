---
status: pending
priority: p1
issue_id: "058"
tags: ["code-review","concurrency"]
dependencies: []
---

# Task.Run in _OnOpenTimerElapsed can resume callback after DisposeAsync

## Problem Statement

CircuitBreakerStateManager._OnOpenTimerElapsed (line 739) fires a fire-and-forget Task.Run that calls resumeCallback(). When DisposeAsync is called, it cancels _disposalCts and then awaits all timer disposals. However, Timer.DisposeAsync() only waits for the callback THREAD to finish — not for posted Task.Run workitems. The Task.Run workitem can be queued after DisposeAsync considers the timer disposed, then begin executing on the thread pool after CircuitBreakerStateManager is fully disposed. The CancellationToken check at line 741 partially mitigates this, but there is a window where the task passes the check before DisposeAsync cancels, then resumeCallback() executes on a disposed object. Additionally, the ContinueWith at lines 754-763 boxes a (ILogger, string) value tuple to avoid a closure allocation on a cold path (circuit trips are rare), then immediately unboxes it — the optimization is counterproductive and the AggregateException it logs is not unwrapped, hiding the real inner exception.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:739-764
- **Discovered by:** strict-dotnet-reviewer (P1.2, P1.3), performance-oracle (P1), simplicity-reviewer (P2)
- **Secondary issue:** ContinueWith logs t.Exception (AggregateException), inner exception from _ReopenAfterResumeFailureAsync not surfaced

## Proposed Solutions

### Track Task.Run reference per GroupCircuitState and await it in DisposeAsync
- **Pros**: Proper structured lifetime, closes the post-disposal race completely
- **Cons**: Requires storing CancellableTask per group state
- **Effort**: Medium
- **Risk**: Low

### Flatten to simple async lambda with inline catch, remove ContinueWith
- **Pros**: Eliminates boxing, surfaces inner exception directly, simpler code
- **Cons**: Does not fully close post-disposal race (still fire-and-forget)
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Flatten the ContinueWith (removes boxing and AggregateException issue) AND track the Task per group to await in DisposeAsync. Use `logger.LogError(ex, ...)` inside catch to surface the real exception.

## Acceptance Criteria

- [ ] resumeCallback() cannot execute after DisposeAsync returns
- [ ] Exception from _ReopenAfterResumeFailureAsync is logged with the inner exception, not wrapped AggregateException
- [ ] No ValueTuple boxing on the cold HalfOpen path
- [ ] Existing HalfOpen test coverage still passes

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
