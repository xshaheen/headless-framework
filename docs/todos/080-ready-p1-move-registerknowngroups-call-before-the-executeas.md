---
status: ready
priority: p1
issue_id: "080"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Move RegisterKnownGroups call before the ExecuteAsync foreach loop

## Problem Statement

In IConsumerRegister.ExecuteAsync, _circuitBreakerStateManager?.RegisterKnownGroups(groupingMatches.Keys) is called inside the foreach loop over groupingMatches. This means the first group gets RegisterKnownGroups called mid-enumeration, not before group setup begins. If a failure arrives for the first group between it being registered with callbacks and RegisterKnownGroups completing, the cardinality guard fires a LogWarning for a legitimate registered group.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs (ExecuteAsync, ~line 3476)
- **Risk:** Spurious LogWarning for legitimate groups; OTel cardinality guard fires incorrectly
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

### Call RegisterKnownGroups(groupingMatches.Keys) before the foreach loop begins
- **Pros**: Trivial 1-line move, eliminates the race
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Move `_circuitBreakerStateManager?.RegisterKnownGroups(groupingMatches.Keys)` to be called once before the `foreach (var matchGroup in groupingMatches)` loop.

## Acceptance Criteria

- [ ] RegisterKnownGroups called before any group's callbacks are registered
- [ ] No LogWarning for legitimate registered groups during startup

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
