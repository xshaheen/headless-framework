---
status: done
priority: p2
issue_id: "024"
tags: ["code-review","messaging","circuit-breaker","performance"]
dependencies: []
---

# Actually enforce single-probe half-open behavior

## Problem Statement

CircuitBreakerStateManager defines a ProbePermit semaphore, but nothing ever waits on it. When the timer flips a group to HalfOpen, every resumed consumer can immediately send messages, so the breaker never limits recovery to a single probe and can stampede an unhealthy dependency.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:338
- **Impact:** Half-open behaves like unrestricted open, defeating the backpressure design.

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

_To be determined during triage._

## Acceptance Criteria

- [ ] Half-open acquisition gates all but one probe
- [ ] Only the probe message can close or re-open the circuit

## Notes

From PR #194 review.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: pending → done
