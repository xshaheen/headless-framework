---
status: ready
priority: p1
issue_id: "023"
tags: ["code-review","threading","correctness"]
dependencies: []
---

# Fix Task.Run resume callback race with Dispose in _OnOpenTimerElapsed

## Problem Statement

In CircuitBreakerStateManager._OnOpenTimerElapsed (line 669), Task.Run fire-and-forget can invoke resumeCallback after Dispose has set _disposed=1 and nulled OnResume. The Volatile.Read check inside the lambda races with Dispose — the callback captures a local copy of resumeCallback before Dispose runs, so the null-out in Dispose has no effect. The resume callback executes against a potentially torn-down object graph (disposed ConsumerRegister and transport clients).

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:669-684
- **Risk:** High — resume callback on disposed transport clients can throw or corrupt state
- **Discovered by:** strict-dotnet-reviewer, performance-oracle
- **Known Pattern:** docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md — Pattern 6: Fire-and-forget Task.Run must observe exceptions

## Proposed Solutions

### CancellationTokenSource linked to disposal
- **Pros**: Clean, race-free cancellation; standard .NET pattern
- **Cons**: Adds one more CTS to manage
- **Effort**: Small
- **Risk**: Low

### Track Task and await in DisposeAsync
- **Pros**: Guarantees callback completion before disposal
- **Cons**: Requires IAsyncDisposable; current class only implements IDisposable
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Create a CancellationTokenSource cancelled in Dispose. Pass its token into Task.Run lambda and check ct.IsCancellationRequested at the start. This gives race-free cancellation without requiring IAsyncDisposable.

## Acceptance Criteria

- [ ] Task.Run lambda checks CancellationToken before invoking resumeCallback
- [ ] CancellationTokenSource is cancelled in Dispose before nulling callbacks
- [ ] No unobserved task exceptions — attach ContinueWith(OnlyOnFaulted) for logging
- [ ] Unit test: Dispose during HalfOpen transition does not invoke resume callback

## Notes

Prior solution doc (Pattern 6) recommends ContinueWith for unobserved exceptions. Combine both fixes.

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-22 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
