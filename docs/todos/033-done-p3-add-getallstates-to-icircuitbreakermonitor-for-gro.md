---
status: done
priority: p3
issue_id: "033"
tags: ["code-review","dotnet","architecture"]
dependencies: []
---

# Add GetAllStates() to ICircuitBreakerMonitor for group enumeration

## Problem Statement

ICircuitBreakerMonitor.IsOpen and GetState both require the caller to know the group name. An agent asked 'are any circuits currently open?' must resolve all group names from IConsumerRegistry and probe each one. There is no GetAllStates() that returns all tracked groups and their current state. This also blocks a dashboard endpoint that needs to show all circuit states.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs
- **Discovered by:** compound-engineering:review:agent-native-reviewer

## Proposed Solutions

### Add IReadOnlyDictionary<string, CircuitBreakerState> GetAllStates()
- **Pros**: Full enumeration, enables dashboard + health checks
- **Cons**: Wider public surface
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add GetAllStates() returning a snapshot of all tracked group states. Implement by iterating _groups in CircuitBreakerStateManager.

## Acceptance Criteria

- [ ] GetAllStates() returns all groups with non-Closed state at minimum
- [ ] Dashboard endpoint can use it

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
