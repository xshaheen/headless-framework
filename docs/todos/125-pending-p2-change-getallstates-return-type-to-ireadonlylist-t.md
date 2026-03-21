---
status: pending
priority: p2
issue_id: "125"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Change GetAllStates return type to IReadOnlyList to prevent async-boundary footgun

## Problem Statement

ICircuitBreakerMonitor.GetAllStates() returns IEnumerable. If a consumer stores the enumerable and iterates it after an await, circuit states may have changed between the call and iteration. The XML doc warns about this but the type provides no enforcement. Agent code that stores the enumerable for later iteration will silently get stale data.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs:27-29
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:285-291
- **Risk:** Silent stale snapshot if enumerable stored across await boundary
- **Discovered by:** compound-engineering:review:agent-native-reviewer, compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

### Change return type to IReadOnlyList; materialize via ToList() in implementation
- **Pros**: Forces immediate materialization; snapshot semantics enforced by type system
- **Cons**: Allocation per call — acceptable since not a hot path
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change interface return type to IReadOnlyList. Update implementation to materialize with ToList().

## Acceptance Criteria

- [ ] GetAllStates returns IReadOnlyList
- [ ] Snapshot materialized at call time
- [ ] XML doc updated

## Notes

PR #194 second-pass review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
