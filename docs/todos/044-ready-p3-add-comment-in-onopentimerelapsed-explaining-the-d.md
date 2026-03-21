---
status: ready
priority: p3
issue_id: "044"
tags: ["code-review","dotnet","quality"]
dependencies: []
---

# Add comment in _OnOpenTimerElapsed explaining the Dispose+in-flight-callback invariant

## Problem Statement

In _TransitionToOpen, state.OpenTimer?.Dispose() is called before creating a new timer. Timer.Dispose() does not guarantee in-flight callbacks will not fire. The safety comes from _OnOpenTimerElapsed checking if (state.State is not CircuitBreakerState.Open) return. This two-piece invariant is not obvious and must be understood together to convince a reader the code is safe. A future maintainer removing the guard will create a race.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:184-191
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs - _OnOpenTimerElapsed
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer

## Proposed Solutions

### Add cross-reference comments
- **Pros**: Self-documenting
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add comment in _TransitionToOpen: Dispose does not cancel in-flight callbacks; _OnOpenTimerElapsed guards against stale state. Add comment in _OnOpenTimerElapsed: Guards against stale callback after timer was disposed and recreated in _TransitionToOpen.

## Acceptance Criteria

- [ ] Both locations have cross-reference comments explaining the invariant

## Notes

Discovered during PR #194 code review (round 2)

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
