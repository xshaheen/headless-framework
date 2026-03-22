---
status: done
priority: p1
issue_id: "039"
tags: ["code-review","threading","correctness"]
dependencies: []
---

# Fix _pauseGate/_paused race condition in all transport PauseAsync/ResumeAsync

## Problem Statement

Between Interlocked.CompareExchange(ref _paused, 1, 0) and the assignment to _pauseGate (new TaskCompletionSource), a concurrent ResumeAsync can observe _paused=1 and call TrySetResult on the OLD gate (already completed). Resume then sets _paused=0, but _pauseGate is the NEW incomplete TCS. The consumer enters the pause gate and blocks forever. This race exists in all 8 transport implementations.

## Findings

- **Location:** All transport PauseAsync/ResumeAsync: RabbitMQ, Kafka, SQS, ASB, Redis, NATS, Pulsar, InMemory
- **Risk:** Critical — consumer permanently blocked under circuit breaker oscillation
- **Discovered by:** strict-dotnet-reviewer
- **Known Pattern:** docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md — Pattern 2: volatile bool to Interlocked.CompareExchange

## Proposed Solutions

### Lock around both CAS and TCS assignment
- **Pros**: Eliminates the race completely; Pause/Resume are infrequent so lock cost is negligible
- **Cons**: Adds a lock object to each transport
- **Effort**: Medium
- **Risk**: Low

### Extract ConsumerPauseGate helper class
- **Pros**: Eliminates duplication across 7 transports AND fixes the race in one place
- **Cons**: Larger refactor
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Extract a ConsumerPauseGate class with correct locking that all transports compose. Fixes the race AND eliminates the 7-transport duplication. The lock protects only Pause/Resume (rare), not the message processing hot path.

## Acceptance Criteria

- [ ] No window between _paused flag and _pauseGate assignment observable by concurrent callers
- [ ] All 8 transports use the same correct implementation
- [ ] Consumer never permanently blocks under rapid pause/resume cycling
- [ ] Unit test: concurrent Pause+Resume does not deadlock

## Notes

Prior solution doc Pattern 2 flagged this. The simplicity reviewer also noted _CreateCompletedGate() duplicated across 7 clients — this fix addresses both.

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
