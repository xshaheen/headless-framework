---
status: pending
priority: p1
issue_id: "100"
tags: ["code-review","dotnet","thread-safety","circuit-breaker"]
dependencies: []
---

# TOCTOU race in transport PauseAsync/ResumeAsync across multiple transports

## Problem Statement

Transport consumer clients check _pauseGate.IsPaused before calling PauseAsync/ResumeAsync, then execute transport-level actions. Two concurrent callers can both pass the IsPaused check, both call PauseAsync(), and both execute transport-level operations (e.g. BasicCancelAsync on RabbitMQ). While ConsumerPauseGate is internally idempotent, double transport-level calls may cause channel exceptions.

## Findings

- **RabbitMQ:** src/Headless.Messaging.RabbitMq/RabbitMqConsumerClient.cs:134-144
- **Kafka:** src/Headless.Messaging.Kafka/KafkaConsumerClient.cs:171-177
- **AzureServiceBus:** src/Headless.Messaging.AzureServiceBus/AzureServiceBusConsumerClient.cs:145-155
- **NATS:** src/Headless.Messaging.Nats/NatsConsumerClient.cs:309-319
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer, docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md

## Proposed Solutions

### Return bool from ConsumerPauseGate.PauseAsync/ResumeAsync
- **Pros**: Eliminates TOCTOU entirely, clean API
- **Cons**: Breaking internal API change
- **Effort**: Small
- **Risk**: Low

### Remove pre-check, rely on gate idempotency + guard transport action
- **Pros**: Minimal change
- **Cons**: Still relies on checking IsPaused after gate call
- **Effort**: Small
- **Risk**: Medium


## Recommended Action

Option 1: Make PauseAsync/ResumeAsync return bool indicating whether a transition occurred. Transports conditionally execute transport-level work based on the return value.

## Acceptance Criteria

- [ ] ConsumerPauseGate.PauseAsync returns bool (true = transitioned)
- [ ] ConsumerPauseGate.ResumeAsync returns bool (true = transitioned)
- [ ] All transport PauseAsync/ResumeAsync use return value to guard transport-level actions
- [ ] No TOCTOU gap between check and action

## Notes

Known pattern from docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md — recommends Interlocked.CompareExchange approach.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
