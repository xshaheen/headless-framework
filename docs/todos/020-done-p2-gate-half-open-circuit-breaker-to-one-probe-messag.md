---
status: done
priority: p2
issue_id: "020"
tags: ["code-review","concurrency","dos"]
dependencies: []
---

# Gate half-open circuit breaker to one probe message

## Problem Statement

CircuitBreakerStateManager transitions Open -> HalfOpen and immediately resumes the whole consumer group, but the ProbePermit semaphore is never acquired anywhere. That means half-open does not actually limit intake to a single probe, so one recovery attempt can reopen full traffic and let many concurrent failures slip through before the breaker trips again.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:263
- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:269
- **Risk:** Half-open state does not restrict traffic to a single probe, defeating backpressure

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

_To be determined during triage._

## Acceptance Criteria

- [ ] Half-open admits at most one probe message per group
- [ ] Full consumer resume happens only after a successful probe
- [ ] Regression test covers concurrent half-open arrivals

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
