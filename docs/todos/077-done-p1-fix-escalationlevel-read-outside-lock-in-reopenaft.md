---
status: done
priority: p1
issue_id: "077"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix EscalationLevel read outside lock in _ReopenAfterResumeFailureAsync log

## Problem Statement

In CircuitBreakerStateManager._ReopenAfterResumeFailureAsync, EscalationLevel is incremented inside lock(groupLock) but then read again outside the lock for the LogCritical call. Another thread can modify EscalationLevel between the lock release and the log read, causing the log to show a stale or incorrect escalation level. This is a data race on a mutable field.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs (in _ReopenAfterResumeFailureAsync)
- **Risk:** Data race — stale log value under concurrent escalation
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer

## Proposed Solutions

### Capture escalation level inside the lock before releasing
- **Pros**: Trivial fix, no behavioral change, correct log value
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Capture the value inside the lock: `int escalation; lock(groupLock) { state.EscalationLevel++; escalation = state.EscalationLevel; }` then pass `escalation` to LogCritical instead of `state.EscalationLevel`.

## Acceptance Criteria

- [ ] EscalationLevel captured inside lock before release
- [ ] Captured value used in LogCritical call
- [ ] No read of state.EscalationLevel outside lock in this method

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
