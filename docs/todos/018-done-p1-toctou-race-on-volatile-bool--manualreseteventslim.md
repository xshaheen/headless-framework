---
status: done
priority: p1
issue_id: "018"
tags: ["code-review","messaging","circuit-breaker","transport","dotnet"]
dependencies: []
---

# TOCTOU race on volatile bool + ManualResetEventSlim in 5 transport PauseAsync/ResumeAsync

## Problem Statement

All 5 MRES-based transports (SQS, InMemory, NATS, Pulsar, RedisStreams) plus AzureServiceBus use a volatile bool _paused with check-then-act pattern. Two concurrent callers can both pass the if(!_paused) check before either sets _paused=true. For Azure Service Bus, two concurrent StopProcessingAsync calls. For MRES transports: a PauseAsync+ResumeAsync race can leave _paused=true but _pauseGate.IsSet=true (gate open), meaning future PauseAsync sees _paused==true and skips Reset() — the gate stays open despite paused state, bypassing the circuit breaker.

## Findings

- **Location:** src/Headless.Messaging.AwsSqs/AmazonSqsConsumerClient.cs:PauseAsync
- **Location:** src/Headless.Messaging.InMemoryQueue/InMemoryConsumerClient.cs:PauseAsync
- **Location:** src/Headless.Messaging.Nats/NatsConsumerClient.cs:PauseAsync
- **Location:** src/Headless.Messaging.Pulsar/PulsarConsumerClient.cs:PauseAsync
- **Location:** src/Headless.Messaging.RedisStreams/IConsumerClient.Redis.cs:PauseAsync
- **Location:** src/Headless.Messaging.AzureServiceBus/AzureServiceBusConsumerClient.cs:PauseAsync
- **Risk:** Circuit breaker can be bypassed by concurrent Pause/Resume calls
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer, security-sentinel, performance-oracle

## Proposed Solutions

### Use Interlocked.CompareExchange on int flag instead of volatile bool
- **Pros**: Atomic, lock-free, consistent across all transports
- **Cons**: Minor change
- **Effort**: Small
- **Risk**: Low

### Remove _paused flag entirely — use ManualResetEventSlim.IsSet as state (it is idempotent)
- **Pros**: Simpler, fewer moving parts
- **Cons**: MRES.IsSet is not a lock — still need atomic gate+flag coordination
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Replace volatile bool _paused with int _paused (0=running,1=paused) and use Interlocked.CompareExchange(ref _paused,1,0)==0 as gate in PauseAsync, Interlocked.CompareExchange(ref _paused,0,1)==1 in ResumeAsync. Apply to all 6 affected transports.

## Acceptance Criteria

- [ ] Concurrent PauseAsync calls are idempotent with no race
- [ ] Concurrent ResumeAsync calls are idempotent with no race
- [ ] Interleaved Pause+Resume under concurrency does not leave gate in wrong state
- [ ] All 6 transports updated consistently

## Notes

PR #194 review. Note: simplicity-reviewer also suggested removing _paused entirely since MRES.Reset() is idempotent — consider that for MRES transports.

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-20 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
