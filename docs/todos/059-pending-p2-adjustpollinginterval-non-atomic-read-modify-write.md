---
status: pending
priority: p2
issue_id: "059"
tags: ["code-review","concurrency"]
dependencies: []
---

# _AdjustPollingInterval non-atomic read-modify-write on _currentIntervalTicks — race with ResetBackpressureAsync

## Problem Statement

MessageNeedToRetryProcessor._AdjustPollingInterval reads _currentIntervalTicks with Interlocked.Read then writes the doubled value with Interlocked.Exchange (IProcessor.NeedRetry.cs:278-313). Between these two operations, ResetBackpressureAsync can reset the value to base. The subsequent Exchange overwrites the reset with the doubled value computed from the pre-reset read. volatile on cycle counters also does not protect compound increment+compare operations — the comment claiming volatile solves it is incorrect.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:278-313
- **Discovered by:** strict-dotnet-reviewer (P2.2, P3.4), performance-oracle (P2.3)

## Proposed Solutions

### Use Interlocked.CompareExchange CAS loop for _currentIntervalTicks mutation
- **Pros**: Atomic read-modify-write, closes the race with ResetBackpressureAsync
- **Cons**: Slightly more complex loop
- **Effort**: Small
- **Risk**: Low

### Accept the race and document it explicitly
- **Pros**: No code change
- **Cons**: Misleading comment implies volatile solves it when it does not
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Use CAS loop for _currentIntervalTicks. For cycle counters (_consecutiveHealthyCycles, _consecutiveCleanCycles), document that volatile provides visibility but not compound atomicity — the race is accepted as benign approximation.

## Acceptance Criteria

- [ ] _currentIntervalTicks mutation uses CAS loop to prevent reset override race
- [ ] Comment on cycle counters accurately describes the threading model
- [ ] Test: concurrent ResetBackpressureAsync + _AdjustPollingInterval does not permanently override the reset

## Notes

Source: Code review

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
