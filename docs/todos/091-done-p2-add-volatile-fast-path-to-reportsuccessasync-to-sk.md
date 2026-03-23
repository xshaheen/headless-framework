---
status: done
priority: p2
issue_id: "091"
tags: ["code-review","performance","messaging"]
dependencies: []
---

# Add volatile fast path to ReportSuccessAsync to skip lock in Closed steady state

## Problem Statement

CircuitBreakerStateManager.ReportSuccessAsync acquires the per-group lock on every successful message, even when the circuit is Closed and ConsecutiveFailures is already 0 (the common case). At 1000 msg/s this is 1000 unnecessary lock acquisitions per second per group. The lock body in this state is a no-op (resets an already-zero counter, skips HalfOpen branch).

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:~252-283
- **Problem:** Lock acquired on every success, even no-op case in Closed state
- **Impact:** ~1000 lock acquisitions/s per group at 1000 msg/s
- **Discovered by:** performance-oracle

## Proposed Solutions

### Volatile fast-path check before lock acquisition
- **Pros**: Eliminates lock on hot path, volatile read is free on x86/x64
- **Cons**: Stale read possible (acceptable as best-effort)
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add early return before lock: `if (state.State is CircuitBreakerState.Closed && state.ConsecutiveFailures == 0) return;`. The volatile State read and non-locked ConsecutiveFailures read are acceptable as a best-effort fast path.

## Acceptance Criteria

- [ ] No lock acquired in Closed state with ConsecutiveFailures == 0
- [ ] HalfOpen success still correctly transitions to Closed
- [ ] Benchmark confirms reduced lock contention at high message rate

## Notes

PR #194 code review finding.

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
