---
status: ready
priority: p2
issue_id: "068"
tags: ["code-review","architecture"]
dependencies: []
---

# CircuitBreakerSnapshot missing ConsecutiveFailures, FailureThreshold, EffectiveOpenDuration

## Problem Statement

CircuitBreakerSnapshot (CircuitBreakerSnapshot.cs) exposes State, EscalationLevel, OpenedAt, EstimatedRemainingOpenDuration. An agent or operator reasoning about whether to intervene needs the consecutive failure count and effective thresholds to determine proximity to a trip or to understand why the open duration is what it is. These fields are available inside the GroupCircuitState lock scope during GetSnapshot construction.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerSnapshot.cs
- **Discovered by:** agent-native-reviewer (P2)

## Proposed Solutions

### Add ConsecutiveFailures, FailureThreshold, EffectiveOpenDuration to CircuitBreakerSnapshot
- **Pros**: Enables agent reasoning without implementation knowledge
- **Cons**: Minor snapshot size increase
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add the three fields to CircuitBreakerSnapshot and populate them in CircuitBreakerStateManager.GetSnapshot inside the group lock scope.

## Acceptance Criteria

- [ ] CircuitBreakerSnapshot includes ConsecutiveFailures, FailureThreshold, EffectiveOpenDuration
- [ ] GetSnapshot populates all three fields correctly
- [ ] XML docs for new properties
- [ ] Snapshot unit tests verify new fields

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-22 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
