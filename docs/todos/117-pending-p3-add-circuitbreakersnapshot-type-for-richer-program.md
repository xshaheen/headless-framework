---
status: pending
priority: p3
issue_id: "117"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Add CircuitBreakerSnapshot type for richer programmatic state observation

## Problem Statement

ICircuitBreakerMonitor.GetState() returns only CircuitBreakerState (Open/Closed/HalfOpen). An agent or health check that finds a group is Open cannot determine how long it has been open, what escalation level it is at, or when it will transition to HalfOpen. This limits automated decision-making (e.g., 'circuit open for 4 minutes at level 3 — consider manual reset vs waiting').

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/ICircuitBreakerMonitor.cs
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs — GroupCircuitState.EscalationLevel, OpenedAt (private)
- **Risk:** Agent/operator cannot reason about circuit duration or escalation level
- **Discovered by:** compound-engineering:review:agent-native-reviewer

## Proposed Solutions

### Add CircuitBreakerSnapshot record with State, EscalationLevel, OpenedAt, EstimatedRemainingOpenDuration; add GetSnapshot(string) and GetAllSnapshots() to ICircuitBreakerMonitor
- **Pros**: Rich observability; agents can make informed reset decisions
- **Cons**: New type and API methods
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Define CircuitBreakerSnapshot sealed record. Add GetSnapshot(string groupName) returning CircuitBreakerSnapshot? to ICircuitBreakerMonitor.

## Acceptance Criteria

- [ ] CircuitBreakerSnapshot includes State, EscalationLevel, OpenedAt, EstimatedRemainingOpenDuration
- [ ] GetSnapshot returns null for unknown groups
- [ ] ICircuitBreakerMonitor.GetSnapshot registered in DI

## Notes

PR #194 second-pass review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
