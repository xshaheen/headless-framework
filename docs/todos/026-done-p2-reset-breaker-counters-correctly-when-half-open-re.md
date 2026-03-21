---
status: done
priority: p2
issue_id: "026"
tags: ["code-review","dotnet","correctness","circuit-breaker"]
dependencies: []
---

# Reset breaker counters correctly when half-open recovers or relapses

## Problem Statement

CircuitBreakerStateManager closes the circuit from HalfOpen without clearing ConsecutiveFailures on the non-transient-failure path, and it increments SuccessfulCyclesAfterClose without clearing that streak when the breaker opens again. In practice, one bad half-open message can leave the closed circuit one failure away from reopening, and escalation can reset after three total closes rather than three consecutive healthy recovery cycles.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:62
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:83
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:175
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:206
- **Risk:** Medium - the breaker can reopen too aggressively after a recovered dependency or de-escalate too early after intermittent outages

## Proposed Solutions

### Reset failure and healthy-cycle counters in transition methods
- **Pros**: Keeps state-machine invariants local and explicit
- **Cons**: Requires careful coverage of each transition
- **Effort**: Small
- **Risk**: Low

### Represent recovery streaks explicitly in tests and state
- **Pros**: Makes intended semantics unambiguous
- **Cons**: Slightly larger refactor
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Treat HalfOpen -> Closed as a fresh healthy baseline regardless of whether the closing signal was success or a non-transient bad message, and clear any healthy-cycle streak whenever the breaker transitions back to Open.

## Acceptance Criteria

- [ ] Closing from HalfOpen resets ConsecutiveFailures so the next transient failure does not reopen immediately
- [ ] Reopening the breaker clears the healthy-cycle streak used for escalation reset
- [ ] Regression tests cover non-transient half-open failure followed by one transient failure, and an intermittent open/close/open sequence

## Notes

Discovered during PR #194 review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: pending → done
