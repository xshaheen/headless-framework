---
status: done
priority: p1
issue_id: "028"
tags: ["code-review","dotnet","quality"]
dependencies: []
---

# Remove dead ProbePermit SemaphoreSlim and _TryReleaseProbeSemaphore

## Problem Statement

After TryAcquireProbePermit was removed from ICircuitBreakerStateManager (per PR comment fix), the ProbePermit SemaphoreSlim field and _TryReleaseProbeSemaphore method remain in GroupCircuitState and CircuitBreakerStateManager. Nothing ever calls ProbePermit.WaitAsync() (count stays at 1 forever), so _TryReleaseProbeSemaphore (which only releases when count == 0) is a permanent no-op. Dead code that wastes a SemaphoreSlim allocation per group.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs - GroupCircuitState.ProbePermit field
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs - _TryReleaseProbeSemaphore method
- **Risk:** Medium - wasted allocation + confusing dead code
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

### Remove the dead code
- **Pros**: Clean, zero ambiguity
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Remove ProbePermit field from GroupCircuitState, remove _TryReleaseProbeSemaphore method, and remove all calls to it in ReportSuccess and ReportFailureAsync.

## Acceptance Criteria

- [ ] ProbePermit field removed from GroupCircuitState
- [ ] _TryReleaseProbeSemaphore method removed
- [ ] All call sites of _TryReleaseProbeSemaphore removed
- [ ] Tests still pass

## Notes

Discovered during PR #194 code review (round 2)

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
