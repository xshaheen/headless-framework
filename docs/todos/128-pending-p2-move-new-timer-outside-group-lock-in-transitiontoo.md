---
status: pending
priority: p2
issue_id: "128"
tags: ["code-review","dotnet","messaging","performance"]
dependencies: []
---

# Move new Timer() outside group lock in _TransitionToOpen to reduce lock-hold time

## Problem Statement

In CircuitBreakerStateManager._TransitionToOpen, `new Timer(...)` is called while holding the per-group Lock. Timer construction performs heap allocation, ValueTuple boxing, and TimerQueue registration (which may acquire internal runtime locks). Holding the group lock during TimerQueue registration nests two lock acquisitions and increases lock-hold time on every circuit trip.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:451-456
- **Risk:** Increased lock-hold time on trip; two nested lock levels
- **Discovered by:** compound-engineering:review:performance-oracle

## Proposed Solutions

### Capture openDuration and generation inside lock, release lock, create Timer, re-acquire to assign state.OpenTimer
- **Pros**: Reduces lock-hold time; eliminates nested locking
- **Cons**: Slightly more code
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Capture needed values inside lock, release, create Timer, then assign state.OpenTimer under brief re-lock.

## Acceptance Criteria

- [ ] new Timer() called outside group lock
- [ ] Timer state correctly captures generation and groupName
- [ ] State transition tests pass

## Notes

PR #194 second-pass review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
