---
status: pending
priority: p1
issue_id: "013"
tags: ["code-review","dotnet","architecture","performance"]
dependencies: []
---

# Fix broken half-open probe behavior

## Problem Statement

The new half-open circuit-breaker path does not behave like a single-probe recovery state. `CircuitBreakerStateManager` exposes `TryAcquireProbePermit`, but no production code consumes it. When the timer transitions a group to HalfOpen, `ConsumerRegister` resumes every client in the group immediately, so full traffic resumes instead of one probe. At the same time, `MessageNeedToRetryProcessor` still treats HalfOpen as open and skips queued retries, which means a quiet group can stay half-open indefinitely with no message available to close or re-open the circuit.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:125
- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:149
- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:266
- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:179
- **Risk:** High - dependency recovery path can re-amplify outages or stall indefinitely in HalfOpen
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer, performance-oracle, code-simplicity-reviewer

## Proposed Solutions

### Enforce probe permit in dispatch path
- **Pros**: Keeps current state model and metrics with minimal API change
- **Cons**: Requires careful gating across transport and retry paths
- **Effort**: Medium
- **Risk**: Medium

### Keep group paused and schedule an explicit single probe
- **Pros**: Makes half-open semantics explicit and avoids herd resumption
- **Cons**: More orchestration code
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Implement a real single-probe path: only one live or retry message may execute while HalfOpen, and quiet groups must still have a deterministic probe source.

## Acceptance Criteria

- [ ] At most one message is allowed through for a group while HalfOpen
- [ ] HalfOpen groups can transition to Closed or Open even when live traffic is idle
- [ ] Retry backlog does not remain permanently blocked once a group reaches HalfOpen
- [ ] Tests cover multi-consumer half-open recovery and a quiet-group retry/backlog scenario

## Notes

Discovered during PR #194 code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
