---
status: done
priority: p2
issue_id: "021"
tags: ["code-review","validation","dos"]
dependencies: []
---

# Reject zero-duration circuit breaker open windows

## Problem Statement

CircuitBreakerOptionsValidator allows OpenDuration <= TimeSpan.Zero, so a misconfigured breaker can transition Open -> HalfOpen immediately. That defeats the protection window and can cause hot open/half-open oscillation, amplifying load instead of throttling it.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerOptionsValidator.cs:13
- **Risk:** Invalid open durations can collapse the backoff window and create a retry storm

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

_To be determined during triage._

## Acceptance Criteria

- [ ] OpenDuration must be strictly greater than zero
- [ ] Invalid zero or negative durations fail fast during startup
- [ ] Regression test covers zero and negative OpenDuration

## Notes

PR #194 review finding

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: pending → done
