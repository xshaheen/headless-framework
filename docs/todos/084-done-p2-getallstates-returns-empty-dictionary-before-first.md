---
status: done
priority: p2
issue_id: "084"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# GetAllStates returns empty dictionary before first failure — pre-populate known groups

## Problem Statement

ICircuitBreakerMonitor.GetAllStates() iterates _groups which is populated lazily on first _GetOrAddState call. Before any circuit activity, an agent or health check calling GetAllStates() gets an empty dictionary and cannot distinguish all circuits closed and healthy from circuit breaker not yet initialized. This breaks the agent-native observability pattern.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs (RegisterKnownGroups + GetAllStates)
- **Risk:** Agent/health-check observability gap — cannot enumerate groups at startup
- **Discovered by:** agent-native-reviewer

## Proposed Solutions

### Pre-populate _groups with Closed state for all known groups in RegisterKnownGroups
- **Pros**: GetAllStates immediately returns all groups with Closed state
- **Cons**: Slight memory overhead for groups that never have failures
- **Effort**: Small
- **Risk**: Low

### Add KnownGroupNames property to ICircuitBreakerMonitor
- **Pros**: No lazy initialization change needed
- **Cons**: Separate API surface for enumeration vs state
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Pre-populate _groups with Closed state in RegisterKnownGroups. This is the simplest fix and ensures GetAllStates() is correct from startup.

## Acceptance Criteria

- [ ] GetAllStates() returns all registered groups with Closed state immediately after RegisterKnownGroups
- [ ] Test verifies GetAllStates before any failure returns all known groups as Closed

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
