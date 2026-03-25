---
status: pending
priority: p2
issue_id: "004"
tags: ["code-review","dotnet","quality","nats","concurrency"]
dependencies: []
---

# Prevent NATS consumer semaphore leak on cancellation

## Problem Statement

The concurrent NATS receive path waits on `_semaphore` and then schedules `Task.Run(..., cancellationToken)`. If cancellation happens after the wait succeeds but before the work item starts, the delegate never runs and the permit is never released, permanently reducing concurrency and potentially stalling the consumer.

## Findings

- **Location:** src/Headless.Messaging.Nats/NatsConsumerClient.cs:175-202
- **Related learning:** docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md
- **Discovered by:** code review

## Proposed Solutions

### Acquire inside the scheduled delegate
- **Pros**: Eliminates the cancellation window
- **Cons**: Changes task structure
- **Effort**: Small
- **Risk**: Low

### Release on Task.Run scheduling failure
- **Pros**: Minimal diff
- **Cons**: Still leaves a trickier control flow
- **Effort**: Small
- **Risk**: Medium


## Recommended Action

Move semaphore acquisition/release into a code path that cannot be bypassed by task-scheduling cancellation.

## Acceptance Criteria

- [ ] Cancellation cannot leak a semaphore permit
- [ ] Concurrent message processing still respects `groupConcurrent`
- [ ] A regression test covers cancellation between receive and work-item startup

## Notes

Review of branch xshaheen/review-transports on 2026-03-25.

## Work Log

### 2026-03-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
