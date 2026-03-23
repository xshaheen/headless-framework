---
status: done
priority: p1
issue_id: "098"
tags: ["code-review","dotnet","correctness","circuit-breaker"]
dependencies: []
---

# _TransitionToClosed increments SuccessfulCyclesAfterClose for non-transient failure closes

## Problem Statement

In CircuitBreakerStateManager._TransitionToClosed, SuccessfulCyclesAfterClose is always incremented regardless of whether the close was triggered by probe success or non-transient failure. A non-transient failure closing the circuit is NOT a recovery signal — counting it toward escalation reset gives false credit. A dependency that trips repeatedly but always gets closed by bad messages would never properly escalate.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:761-799
- **Root Cause:** probeSucceeded parameter is only used for log message, not counter logic
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

### Only increment on probe success
- **Pros**: Correct semantics, simple fix
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Only increment SuccessfulCyclesAfterClose when probeSucceeded is true. Reset to 0 when probeSucceeded is false (non-transient failure is not a recovery signal).

## Acceptance Criteria

- [ ] SuccessfulCyclesAfterClose only increments when probeSucceeded == true
- [ ] Non-transient failure close resets SuccessfulCyclesAfterClose to 0
- [ ] Unit test covers: repeated non-transient close does not reset escalation

## Notes

The probeSucceeded parameter was already plumbed through but only used for logging, not counter logic.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-23 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
