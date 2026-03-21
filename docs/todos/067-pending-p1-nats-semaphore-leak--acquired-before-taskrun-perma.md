---
status: pending
priority: p1
issue_id: "067"
tags: ["code-review","security","dotnet"]
dependencies: []
---

# NATS semaphore leak — acquired before Task.Run, permanent consumer stall

## Problem Statement

When groupConcurrent > 0, semaphore is acquired at line 174 on NATS callback thread. Release is inside the fire-and-forget Task.Run lambda. If Task.Run fails to schedule (thread pool exhaustion, OOM), the semaphore slot is permanently leaked. After groupConcurrent consecutive failures, all future messages block forever at WaitAsync, stalling the entire NATS consumer group without tripping the circuit breaker.

## Findings

- **Location:** NatsConsumerClient.cs:174,184
- **Risk:** Critical — permanent consumer stall, undetectable by circuit breaker
- **Discovered by:** security-sentinel, learnings-researcher

## Proposed Solutions

### Acquire semaphore inside Task.Run lambda
- **Pros**: Scheduling failure cannot leak semaphore
- **Cons**: Moves backpressure to Task.Run scheduling
- **Effort**: Small
- **Risk**: Low

### try/finally around Task.Run scheduling to release on failure
- **Pros**: Preserves current backpressure semantics
- **Cons**: Slightly more complex
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Acquire semaphore inside Task.Run. Ensures acquire and release are symmetric within the same async scope.

## Acceptance Criteria

- [ ] Semaphore acquired and released in same async scope
- [ ] Task.Run scheduling failure cannot leak semaphore
- [ ] Consumer group not permanently stalled by transient scheduling failures

## Notes

Prior review (2026-03-21) also identified this as P1.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
