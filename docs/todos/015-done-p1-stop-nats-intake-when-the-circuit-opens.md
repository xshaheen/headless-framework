---
status: done
priority: p1
issue_id: "015"
tags: ["code-review","dotnet","performance","security"]
dependencies: []
---

# Stop NATS intake when the circuit opens

## Problem Statement

The NATS transport does not actually pause broker-side message delivery when the circuit opens. `PauseAsync()` only resets a local `ManualResetEventSlim`, while the JetStream push subscription created by `PushSubscribeAsync` remains active. Under sustained load, callbacks continue arriving, block on `_pauseGate.Wait`, and leave messages unacked until `AckWait` expires, causing redelivery churn and memory or thread growth instead of real backpressure.

## Findings

- **Location:** src/Headless.Messaging.Nats/NatsConsumerClient.cs:119
- **Location:** src/Headless.Messaging.Nats/NatsConsumerClient.cs:153
- **Location:** src/Headless.Messaging.Nats/NatsConsumerClient.cs:303
- **Risk:** High - paused circuit can still accumulate blocked callbacks and redeliveries under load
- **Discovered by:** compound-engineering:review:security-sentinel, performance-oracle

## Proposed Solutions

### Drain or unsubscribe the push consumer on pause and recreate it on resume
- **Pros**: Preserves current push-based model while stopping delivery at the broker boundary
- **Cons**: Requires careful subscription lifecycle management
- **Effort**: Medium
- **Risk**: Medium

### Move NATS consumer path to bounded pull-based consumption
- **Pros**: Natural fit for pause and backpressure semantics
- **Cons**: Larger transport refactor
- **Effort**: Large
- **Risk**: Low


## Recommended Action

Stop intake at the broker boundary for NATS. The minimum safe fix is to own the push subscription handles and drain or unsubscribe them on pause, then recreate them on resume.

## Acceptance Criteria

- [ ] Opening the circuit prevents new NATS deliveries from reaching blocked callbacks
- [ ] Paused NATS consumers do not accumulate unbounded waiting callbacks under load
- [ ] Resuming consumption recreates or reactivates the subscription safely
- [ ] Tests cover pause under active message flow and verify no redelivery churn while paused

## Notes

Discovered during PR #194 code review

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
