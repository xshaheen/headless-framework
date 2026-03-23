---
status: ready
priority: p3
issue_id: "090"
tags: ["code-review","simplicity","messaging"]
dependencies: []
---

# Simplify CircuitBreakerStateManager: remove _groupCount in favor of _groups.Count

## Problem Statement

CircuitBreakerStateManager maintains a separate `private int _groupCount` via Interlocked.Increment with a ReferenceEquals winner-check pattern. This is used only to check >= MaxTrackedGroups on the slow path (new group registration). The comment says ConcurrentDictionary.Count is O(N) but at N<=1000 the O(N) walk is microscopic compared to the Timer allocations that happen on the same path.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:41-53,308,683-687
- **Problem:** Premature optimization — O(N) walk at N<=1000 on a slow registration path is negligible
- **Discovered by:** code-simplicity-reviewer

## Proposed Solutions

### Replace _groupCount with _groups.Count
- **Pros**: -20 LOC, removes subtle concurrency pattern (ReferenceEquals winner-check)
- **Cons**: O(N) cap check — acceptable at N<=1000
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Remove _groupCount and _capWarningLogged Interlocked fields. Use `_groups.Count >= MaxTrackedGroups` directly in the cap-check branch of _GetOrAddState. The _capWarningLogged bool can remain as volatile bool.

## Acceptance Criteria

- [ ] _groupCount field removed
- [ ] ReferenceEquals winner-check pattern removed
- [ ] _groups.Count used for cap enforcement
- [ ] All tests still pass

## Notes

PR #194 code review finding. Low priority — purely cosmetic simplification.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
