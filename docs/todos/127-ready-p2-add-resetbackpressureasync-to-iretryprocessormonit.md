---
status: ready
priority: p2
issue_id: "127"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Add ResetBackpressureAsync to IRetryProcessorMonitor for full recovery control

## Problem Statement

ICircuitBreakerMonitor.ResetAsync allows manually recovering a circuit. IRetryProcessorMonitor is read-only. If an agent resets all circuits, the retry processor stays backed off for 2+ cycles waiting for adaptive algorithm recovery. The remediation path is incomplete: an agent can reset circuit state but not retry backoff state.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/IRetryProcessorMonitor.cs
- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs
- **Risk:** Agent cannot drive full recovery — circuit reset without backoff reset causes delayed retry recovery
- **Discovered by:** compound-engineering:review:agent-native-reviewer

## Proposed Solutions

### Add ValueTask ResetBackpressureAsync(CancellationToken ct = default) to IRetryProcessorMonitor
- **Pros**: Enables full recovery in single agent action
- **Cons**: Interface addition
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add ResetBackpressureAsync. Implementation: Interlocked.Exchange(ref _currentIntervalTicks, _baseInterval.Ticks); reset _consecutiveHealthyCycles and _consecutiveCleanCycles to 0.

## Acceptance Criteria

- [ ] ResetBackpressureAsync resets polling interval to base
- [ ] Resets both cycle counters
- [ ] Test verifies immediate interval reset

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
