---
status: pending
priority: p1
issue_id: "078"
tags: ["code-review","messaging","correctness"]
dependencies: []
---

# Fix ReportSuccessAsync unconditionally resetting ConsecutiveFailures in Open state

## Problem Statement

CircuitBreakerStateManager.ReportSuccessAsync (line ~265) resets state.ConsecutiveFailures = 0 regardless of current circuit state. When the circuit is Open, this erases the failure history. When the timer later fires and transitions to HalfOpen, the circuit can close on first probe success despite the underlying failure history having been wiped. This can cause premature circuit closure during a sustained outage.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:263-271
- **Problem:** ConsecutiveFailures reset unconditionally — should only reset in Closed state
- **Scenario:** Retry processor path can call ReportSuccessAsync while circuit is Open, wiping failure history
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Guard reset by current state
- **Pros**: Correct semantics, minimal change
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add state guard: only reset ConsecutiveFailures when state.State is CircuitBreakerState.Closed. The HalfOpen branch handles its own transition logic independently.

## Acceptance Criteria

- [ ] ConsecutiveFailures not reset when circuit is Open
- [ ] ConsecutiveFailures reset when circuit transitions Closed → keep normal
- [ ] HalfOpen success still transitions to Closed correctly
- [ ] Unit test covering success report while circuit is Open

## Notes

PR #194 code review finding.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
