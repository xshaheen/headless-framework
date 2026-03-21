---
status: done
priority: p1
issue_id: "109"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix TCS write-after-dispose race in 4 transport clients

## Problem Statement

In AmazonSqsConsumerClient, PulsarConsumerClient, NatsConsumerClient, and InMemoryConsumerClient: DisposeAsync calls _pauseGate.TrySetResult(true) to unblock ListeningAsync. If PauseAsync fires concurrently (from the HalfOpen timer callback via Task.Run), it replaces _pauseGate with a new incomplete TCS. ListeningAsync is then blocked forever on the new gate with no cancellation path except the overall CancellationToken. This causes 2-second timeout hangs on every restart/shutdown during an open circuit window. Additionally, the volatile TCS write is not atomic with the preceding Interlocked.CompareExchange on _paused — a thread can read _paused=1 but still see the old completed TCS.

## Findings

- **Location:** src/Headless.Messaging.AwsSqs/AmazonSqsConsumerClient.cs:189-211
- **Location:** src/Headless.Messaging.Pulsar/PulsarConsumerClient.cs:108-116
- **Location:** src/Headless.Messaging.Nats/NatsConsumerClient.cs:304-317
- **Location:** src/Headless.Messaging.InMemoryQueue/InMemoryConsumerClient.cs:124-131
- **Risk:** Shutdown hang; ListeningAsync blocked forever if PauseAsync fires post-dispose
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer, compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

### Add _disposed guard in PauseAsync for all 4 transports
- **Pros**: Consistent with CircuitBreakerStateManager._OnOpenTimerElapsed pattern already in this PR
- **Cons**: 4 files to update
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add `private int _disposed;` field to each transport. In PauseAsync, add `if (Volatile.Read(ref _disposed) != 0) return ValueTask.CompletedTask;` guard. In DisposeAsync, set `Interlocked.Exchange(ref _disposed, 1)` before completing the gate.

## Acceptance Criteria

- [ ] PauseAsync no-ops if called after DisposeAsync in all 4 transports
- [ ] No shutdown hang when circuit is open during restart
- [ ] Interlocked/TCS write ordering correct

## Notes

PR #194 second-pass review.

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
