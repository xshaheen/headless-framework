---
status: done
priority: p3
issue_id: "038"
tags: ["code-review","dotnet","quality"]
dependencies: []
---

# Add cancellation token to _semaphore.WaitAsync() in NatsConsumerClient

## Problem Statement

NatsConsumerClient._SubscriptionMessageHandler calls await _semaphore.WaitAsync() without a cancellation token. If the consumer is stopping, this wait can block indefinitely when all semaphore slots are held by in-flight messages, delaying graceful shutdown.

## Findings

- **Location:** src/Headless.Messaging.Nats/NatsConsumerClient.cs - _SubscriptionMessageHandler
- **Discovered by:** compound-engineering:review:performance-oracle

## Proposed Solutions

### Pass _cancellationToken to WaitAsync
- **Pros**: Clean shutdown
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change to await _semaphore.WaitAsync(_cancellationToken).ConfigureAwait(false).

## Acceptance Criteria

- [ ] _semaphore.WaitAsync passes cancellation token
- [ ] Graceful shutdown unblocks waiting handlers

## Notes

Discovered during PR #194 code review (round 2)

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
