---
status: done
priority: p1
issue_id: "079"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix Task.Run in timer callback invoking callbacks against potentially disposed manager

## Problem Statement

In CircuitBreakerStateManager._OnOpenTimerElapsed, after the lock is released and resumeCallback is captured, a Task.Run fires. If Dispose() is called concurrently — after the callback passed the _disposed guard but before Task.Run executes — the Task.Run closure may call state.OnPause on transport state that has already been disposed, since Dispose nulled OnPause after the callback captured resumeCallback (which is a live reference). This is a low-probability but real race during host shutdown while a circuit is transitioning.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs (timer callback _OnOpenTimerElapsed, ~line 2627)
- **Risk:** Callbacks invoked against disposed transport during shutdown race
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Pass CancellationToken linked to manager lifetime to Task.Run
- **Pros**: Clean cancellation semantics, aligns with .NET patterns
- **Cons**: Requires plumbing a CancellationToken from Dispose
- **Effort**: Small
- **Risk**: Low

### Check _disposed flag inside the Task.Run lambda before invoking callbacks
- **Pros**: Minimal change
- **Cons**: Still a narrow window, but reduces probability significantly
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add an internal CancellationTokenSource linked to Dispose. Pass its token to Task.Run. Inside the lambda, check ct.IsCancellationRequested before invoking resumeCallback and pauseCallback.

## Acceptance Criteria

- [ ] Task.Run lambda checks disposal/cancellation before invoking transport callbacks
- [ ] No callbacks invoked after manager is disposed
- [ ] Test added: concurrent Dispose + timer callback does not invoke callbacks

## Notes

PR #194.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
