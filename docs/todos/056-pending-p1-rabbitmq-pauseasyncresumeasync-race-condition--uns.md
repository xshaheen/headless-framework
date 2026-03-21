---
status: pending
priority: p1
issue_id: "056"
tags: ["code-review","security","dotnet"]
dependencies: []
---

# RabbitMQ PauseAsync/ResumeAsync race condition — unsynchronized volatile bool

## Problem Statement

PauseAsync checks _paused and calls BasicCancelAsync without any lock or Interlocked. Concurrent PauseAsync calls can double-cancel a consumer tag (not idempotent, throws). ResumeAsync can race against PauseAsync causing BasicConsumeAsync during mid-cancel. Kafka and NATS use Interlocked.CompareExchange correctly — RabbitMQ does not.

## Findings

- **Location:** RabbitMqConsumerClient.cs:24,127-144
- **Risk:** Critical — double cancel throws, concurrent pause+resume corrupts channel state
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Replace volatile bool with Interlocked.CompareExchange(ref _paused, 1, 0)
- **Pros**: Matches KafkaConsumerClient and NatsConsumerClient patterns
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Use Interlocked.CompareExchange matching the Kafka/NATS pattern.

## Acceptance Criteria

- [ ] PauseAsync uses Interlocked.CompareExchange for state transition
- [ ] ResumeAsync uses Interlocked.CompareExchange for state transition
- [ ] Double-pause and double-resume are no-ops
- [ ] Concurrent pause+resume does not corrupt channel

## Notes

RabbitMQ BasicCancelAsync is not idempotent — double-cancel throws.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
