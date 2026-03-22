---
status: done
priority: p3
issue_id: "061"
tags: ["code-review","quality"]
dependencies: []
---

# Redundant double state check in ReportFailureAsync:164 — dead guard

## Problem Statement

CircuitBreakerStateManager.ReportFailureAsync default branch (line 155) already gates on '!isTransient || state.State is not CircuitBreakerState.Closed' — anything reaching line 164 is guaranteed to be in Closed state with a transient failure. The second 'state.State is CircuitBreakerState.Closed' check at line 164 is dead code that adds cognitive noise.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:155, 164
- **Discovered by:** simplicity-reviewer (P2)

## Proposed Solutions

### Remove the redundant 'state.State is CircuitBreakerState.Closed &&' from line 164
- **Pros**: Clearer code, dead code removed
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Delete the redundant state check. Add a comment on line 164 noting that Closed state is guaranteed by the guard at line 155 if clarification is needed.

## Acceptance Criteria

- [ ] Redundant state.State is Closed check removed from line 164
- [ ] No behavior change
- [ ] Tests still pass

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-22 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-22 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
