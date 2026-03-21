---
status: done
priority: p1
issue_id: "108"
tags: ["code-review","dotnet","messaging","kafka"]
dependencies: []
---

# Add pause gate to KafkaConsumerClient — rebalance wipes broker-level pause

## Problem Statement

KafkaConsumerClient uses broker-level consumer.Pause(partitions) as its only pause mechanism. When a Kafka rebalance occurs while the circuit is open, the rebalanced partitions are assigned in an un-paused state and ListeningAsync resumes consuming messages. Those messages fail TryAcquireHalfOpenProbe (circuit still Open) and produce a nack storm. All other transports (SQS, NATS, Pulsar, InMemory) have a TCS-based _pauseGate that is independent of broker state.

## Findings

- **Location:** src/Headless.Messaging.Kafka/KafkaConsumerClient.cs:96
- **Risk:** Message nack storm on Kafka rebalance during open circuit window
- **Discovered by:** compound-engineering:review:pragmatic-dotnet-reviewer

## Proposed Solutions

### Add volatile TaskCompletionSource<bool> _pauseGate with Interlocked _paused, matching SQS/NATS pattern
- **Pros**: Consistent with all other transports; survives rebalance
- **Cons**: ~15 lines
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add `private volatile TaskCompletionSource<bool> _pauseGate = _CreateCompletedGate()` and `await _pauseGate.Task.WaitAsync(cancellationToken)` at the top of the polling loop in ListeningAsync.

## Acceptance Criteria

- [ ] Kafka pause survives partition rebalance
- [ ] ListeningAsync blocks during circuit-open regardless of broker state
- [ ] PauseAsync/ResumeAsync use TCS gate consistently with other transports

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
