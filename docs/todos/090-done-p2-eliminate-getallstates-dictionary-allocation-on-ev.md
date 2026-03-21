---
status: done
priority: p2
issue_id: "090"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Eliminate GetAllStates Dictionary allocation on every OTel scrape

## Problem Statement

CircuitBreakerStateManager.GetAllStates() allocates a new Dictionary<string, CircuitBreakerState> on every OTel scrape cycle. With 100 groups at 1-second scrape interval, this generates ~8KB of Dictionary allocations per second. The _ObserveCircuitStates iterator immediately iterates the snapshot, making the intermediate dictionary unnecessary.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs (GetAllStates, ~line 2368), CircuitBreakerMetrics.cs (_ObserveCircuitStates)
- **Risk:** GC pressure on OTel scrape hot path — ~8MB/s at 100 groups with 1s scrape
- **Discovered by:** performance-oracle, strict-dotnet-reviewer

## Proposed Solutions

### Pass _groups ConcurrentDictionary reference directly to OTel callback, iterate inline
- **Pros**: Zero allocation on scrape — ConcurrentDictionary enumeration is safe
- **Cons**: Minor refactor of OTel callback registration
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Register a callback that iterates _groups directly and yields Measurement<int> from state.State (Volatile.Read is already used on GroupCircuitState.State). Eliminates the intermediate snapshot dictionary.

## Acceptance Criteria

- [ ] No new Dictionary allocated during OTel scrape
- [ ] OTel still emits correct per-group state measurements
- [ ] Test verifies OTel callback produces correct measurements

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
