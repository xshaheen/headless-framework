---
status: ready
priority: p2
issue_id: "039"
tags: ["code-review","dotnet","quality"]
dependencies: []
---

# Remove IsOpen duplication between ICircuitBreakerStateManager and ICircuitBreakerMonitor

## Problem Statement

IsOpen(string groupName) is declared in both ICircuitBreakerStateManager and ICircuitBreakerMonitor. ICircuitBreakerStateManager should extend ICircuitBreakerMonitor (or simply not redeclare IsOpen). The duplication means any change to the method signature must be made in two places. It also makes the interface hierarchy inconsistent — ICircuitBreakerStateManager is internal but the duplication is still a code smell.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerStateManager.cs
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs
- **Risk:** Low - maintenance issue
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

### Make ICircuitBreakerStateManager extend ICircuitBreakerMonitor
- **Pros**: No duplication, correct inheritance
- **Cons**: Minor refactor
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Make internal ICircuitBreakerStateManager : ICircuitBreakerMonitor and remove IsOpen and GetState from ICircuitBreakerStateManager (they come from ICircuitBreakerMonitor).

## Acceptance Criteria

- [ ] IsOpen declared once (in ICircuitBreakerMonitor)
- [ ] ICircuitBreakerStateManager extends ICircuitBreakerMonitor
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
