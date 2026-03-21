---
status: pending
priority: p1
issue_id: "081"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix NATS PauseAsync _DrainSubscriptions blocking async path synchronously

## Problem Statement

NatsConsumerClient.PauseAsync is declared as ValueTask (async signature) but internally calls _DrainSubscriptions which acquires _connectionLock and calls sub.Drain(5000ms timeout) synchronously. This blocks the calling thread (the circuit breaker's timer callback path via Task.Run) for up to 5 seconds per subscription, potentially compounding with multiple topics.

## Findings

- **Location:** src/Headless.Messaging.Nats/NatsConsumerClient.cs (PauseAsync → _DrainSubscriptions)
- **Risk:** Up to 5s blocking per subscription per pause — stalls timer callback thread
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

### Make _DrainSubscriptions async with zero/best-effort timeout for the pause path
- **Pros**: Non-blocking pause
- **Cons**: May not drain all in-flight messages before pause completes
- **Effort**: Medium
- **Risk**: Medium

### Cancel subscription without drain (consistent with other transports' instant pause)
- **Pros**: Simple, consistent with RabbitMQ BasicCancelAsync pattern
- **Cons**: In-flight messages may be lost (but circuit is opening due to failures anyway)
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Align with other transports — use cancel without drain for the pause path. In-flight messages during circuit open are already failing (that's why the circuit opened). A 5-second synchronous drain adds latency with no safety benefit.

## Acceptance Criteria

- [ ] PauseAsync in NatsConsumerClient does not block calling thread for more than ~100ms
- [ ] No synchronous drain with multi-second timeout inside PauseAsync
- [ ] Test verifies PauseAsync returns promptly

## Notes

PR #194.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
