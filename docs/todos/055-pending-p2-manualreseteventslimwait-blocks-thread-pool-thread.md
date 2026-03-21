---
status: pending
priority: p2
issue_id: "055"
tags: ["code-review","performance"]
dependencies: []
---

# ManualResetEventSlim.Wait blocks thread-pool threads during circuit pause

## Problem Statement

_pauseGate.Wait(cancellationToken) is a synchronous kernel-level block on thread-pool threads. When circuit opens, every consumer thread blocks indefinitely (30-240 seconds). With N groups tripped and M threads each, this depletes the ThreadPool causing starvation for unrelated work. Affects SQS, Pulsar, InMemory, and Redis transports.

## Findings

- **Location:** InMemoryConsumerClient.cs:84, PulsarConsumerClient.cs:53, AmazonSqsConsumerClient.cs:75
- **Risk:** High — ThreadPool starvation under sustained circuit-open
- **Discovered by:** performance-oracle

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Replace ManualResetEventSlim with async TaskCompletionSource<bool> gate. For InMemory, switch to Channel<TransportMessage> for async consumption.

## Acceptance Criteria

- [ ] Pause gate uses async waiting (TaskCompletionSource or similar)
- [ ] Thread-pool threads released during pause periods
- [ ] No kernel-level blocking in consumer loops

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
