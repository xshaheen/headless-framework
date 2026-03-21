---
status: pending
priority: p1
issue_id: "060"
tags: ["code-review","security","dotnet"]
dependencies: []
---

# Dispose race with timer callbacks — no group lock held during OpenTimer disposal

## Problem Statement

Dispose iterates _groups.Values and calls state.OpenTimer?.Dispose() without acquiring SyncLock. Timer callback (_OnOpenTimerElapsed) can be in-flight: it acquires SyncLock and sets state to HalfOpen while Dispose nulls OpenTimer without the lock — torn write. After Dispose nulls OpenTimer, the timer callback still holds a GroupCircuitState reference and can fire resume callback on disposed objects.

## Findings

- **Location:** CircuitBreakerStateManager.cs:324-331
- **Risk:** Critical — post-dispose callbacks, ObjectDisposedException, torn writes
- **Discovered by:** strict-dotnet-reviewer, security-sentinel

## Proposed Solutions

### Acquire state.SyncLock in Dispose foreach loop before touching OpenTimer
- **Pros**: Matches pattern in RemoveGroup and ResetAsync
- **Cons**: Slightly longer dispose
- **Effort**: Small
- **Risk**: Low

### Add disposed flag (Interlocked) checked in _OnOpenTimerElapsed
- **Pros**: Prevents all post-dispose callback execution
- **Cons**: Additional state to track
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Acquire SyncLock in Dispose + set OnPause/OnResume to null inside lock. Also add disposed flag checked in timer callback.

## Acceptance Criteria

- [ ] Dispose acquires per-group lock before touching OpenTimer
- [ ] OnPause and OnResume set to null inside lock during dispose
- [ ] Timer callbacks check disposed flag before executing
- [ ] No ObjectDisposedException during concurrent dispose+callback

## Notes

Flagged by 2/7 review agents.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
