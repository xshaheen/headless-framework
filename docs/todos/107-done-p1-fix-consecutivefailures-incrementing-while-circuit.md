---
status: done
priority: p1
issue_id: "107"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix ConsecutiveFailures incrementing while circuit is Open

## Problem Statement

CircuitBreakerStateManager.ReportFailureAsync default arm covers both Closed and Open states. When the circuit is Open, ConsecutiveFailures is still incremented on every transient failure, allowing it to grow indefinitely toward int.MaxValue. The trip threshold check guards on Closed only, so this is functionally benign today but is silently incorrect and a maintenance hazard.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:132-149
- **Risk:** Counter overflow on long outages; incorrect state if counter is ever used outside the Closed trip check
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

### Guard increment with state.State is CircuitBreakerState.Closed
- **Pros**: Semantically correct — only count failures that matter for tripping
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add `state.State is CircuitBreakerState.Closed` guard before the `state.ConsecutiveFailures++` increment in the default arm.

## Acceptance Criteria

- [ ] ConsecutiveFailures not incremented while circuit is Open
- [ ] Existing state transition tests still pass

## Notes

PR #194 second-pass review.

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
