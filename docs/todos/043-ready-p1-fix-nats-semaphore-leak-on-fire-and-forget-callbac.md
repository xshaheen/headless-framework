---
status: ready
priority: p1
issue_id: "043"
tags: ["code-review","dotnet","security","quality"]
dependencies: []
---

# Fix NATS semaphore leak on fire-and-forget callback exception

## Problem Statement

In NatsConsumerClient._SubscriptionMessageHandler, when groupConcurrent > 0, the semaphore is acquired via await _semaphore.WaitAsync() and then consumeAsync() is fired via Task.Run. The semaphore is only released in CommitAsync/RejectAsync. If consumeAsync throws an exception that escapes before reaching CommitAsync/RejectAsync (e.g. OnMessageCallback throws past the inner try-catch), the semaphore is permanently leaked. After groupConcurrent such events, all subsequent messages block indefinitely on _semaphore.WaitAsync(), causing a permanent consumer stall. A crafted malformed message could trigger this path.

## Findings

- **Location:** src/Headless.Messaging.Nats/NatsConsumerClient.cs - _SubscriptionMessageHandler, consumeAsync
- **Risk:** High - permanent consumer stall after groupConcurrent malformed messages
- **Discovered by:** compound-engineering:review:security-sentinel

## Proposed Solutions

### Move semaphore release into consumeAsync finally block
- **Pros**: Unconditional release regardless of callback behavior
- **Cons**: Must remove duplicate release from CommitAsync/RejectAsync for this path
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add a try/finally in consumeAsync that calls _ReleaseSemaphore() in the finally block when groupConcurrent > 0. Also add the missing cancellation token to _semaphore.WaitAsync() to allow clean shutdown.

## Acceptance Criteria

- [ ] Semaphore is always released even when OnMessageCallback throws
- [ ] Consumer does not stall after exception in callback
- [ ] groupConcurrent=0 path unaffected
- [ ] _semaphore.WaitAsync() passes cancellation token

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
