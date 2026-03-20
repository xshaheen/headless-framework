---
status: ready
priority: p2
issue_id: "012"
tags: ["code-review","messaging","transport","nats","dotnet","performance"]
dependencies: []
---

# NATS _pauseGate.Wait() without CancellationToken blocks NATS dispatcher thread

## Problem Statement

NatsConsumerClient._SubscriptionMessageHandler is an event callback invoked on NATS's internal dispatch thread. It calls _pauseGate.Wait() without a CancellationToken. When the circuit opens for a NATS consumer, this blocks NATS's own dispatch thread for the full open duration (up to 4 minutes with max escalation). This can stall all subscriptions on that NATS connection, not just the affected consumer group.

## Findings

- **Location:** src/Headless.Messaging.Nats/NatsConsumerClient.cs:_SubscriptionMessageHandler line 2613
- **Risk:** Blocks NATS dispatcher thread for full open duration — starves other subscriptions
- **Discovered by:** strict-dotnet-reviewer, performance-oracle

## Proposed Solutions

### Pass CancellationToken to _pauseGate.Wait()
- **Pros**: Allows NATS to proceed when consumer is shutting down
- **Cons**: Need to surface a CancellationToken into the handler
- **Effort**: Small
- **Risk**: Low

### Use async-compatible gate (SemaphoreSlim or Channel) instead of ManualResetEventSlim in NATS callback
- **Pros**: Non-blocking
- **Cons**: More significant change
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Pass the per-client CancellationToken to _pauseGate.Wait(cancellationToken) so NATS dispatch can proceed when the consumer is shutting down.

## Acceptance Criteria

- [ ] _pauseGate.Wait has a CancellationToken in NATS callback
- [ ] NATS dispatch thread is not blocked beyond the circuit open duration
- [ ] Other NATS subscriptions on the same connection are not affected by one group's circuit

## Notes

PR #194 review.

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
