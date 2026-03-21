---
status: pending
priority: p3
issue_id: "064"
tags: ["code-review","performance"]
dependencies: []
---

# GetAllStates and TagList allocations per OTel collection cycle

## Problem Statement

GetAllStates allocates new Dictionary on every OTel gauge callback (every 60s). _ObserveCircuitStates allocates List<Measurement<int>> and TagList per group per cycle. With 20 groups: 1 Dictionary + 1 List + 20 TagLists per collection.

## Findings

- **Location:** CircuitBreakerStateManager.cs:259-269, CircuitBreakerMetrics.cs:89-108
- **Discovered by:** performance-oracle, code-simplicity-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Use yield return in _ObserveCircuitStates to avoid List allocation. Iterate _groups directly without snapshot Dictionary. Pre-cache TagList per group name.

## Acceptance Criteria

- [ ] Observable gauge callback allocation-free or minimal

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
