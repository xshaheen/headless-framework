---
status: done
priority: p2
issue_id: "043"
tags: ["code-review","performance"]
dependencies: []
---

# Eliminate ValueTuple boxing on Timer state in _CreateAndAssignOpenTimer

## Problem Statement

Timer constructor state parameter is object?. Passing (groupName, generation) ValueTuple boxes it on every circuit trip (CircuitBreakerStateManager.cs:557). The callback (line 631) unboxes it. One allocation per open-circuit event.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:557,631
- **Discovered by:** performance-oracle

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Pass GroupCircuitState directly as timer state (add GroupName property to it). The state already carries TimerGeneration. Zero extra allocation.

## Acceptance Criteria

- [ ] No ValueTuple boxing in timer creation
- [ ] GroupCircuitState used directly as timer state
- [ ] Callback casts to GroupCircuitState instead of unboxing tuple

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

### 2026-03-22 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
