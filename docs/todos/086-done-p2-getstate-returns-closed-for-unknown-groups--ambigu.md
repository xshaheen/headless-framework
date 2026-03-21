---
status: done
priority: p2
issue_id: "086"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# GetState returns Closed for unknown groups — ambiguous vs healthy-and-closed

## Problem Statement

ICircuitBreakerMonitor.GetState returns CircuitBreakerState.Closed for groups not in _groups. This is indistinguishable from a healthy group. An agent or health check enumerating groups cannot verify its input group names are actually registered with the circuit breaker.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs (GetState, ~line 260)
- **Risk:** Silent misuse — wrong group names appear healthy
- **Discovered by:** agent-native-reviewer

## Proposed Solutions

### Return CircuitBreakerState? (nullable) — null means group not tracked
- **Pros**: Unambiguous distinction between not-tracked and closed-healthy
- **Cons**: Breaking API change on ICircuitBreakerMonitor
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change CircuitBreakerState GetState(string groupName) to CircuitBreakerState? GetState(string groupName) returning null for unregistered groups. If the pre-populate fix (084) is applied, this becomes less critical but still valuable.

## Acceptance Criteria

- [ ] GetState returns null for groups not registered via RegisterKnownGroups
- [ ] All existing callers handle the nullable return
- [ ] Test verifies null returned for unknown group name

## Notes

PR #194. Less critical if todo 084 (pre-populate) is implemented.

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
