---
status: done
priority: p2
issue_id: "050"
tags: ["code-review","dotnet","quality","performance"]
dependencies: []
---

# Synchronize adaptive polling counter fields in MessageNeedToRetryProcessor

## Problem Statement

_currentInterval, _consecutiveHealthyCycles, and _consecutiveCleanCycles in MessageNeedToRetryProcessor are plain fields with no synchronization. _AdjustPollingInterval mutates all three. If ProcessAsync is ever re-entered concurrently (e.g., host scheduler calls ProcessAsync while _ProcessReceivedAsync is still running via fire-and-forget Task.Factory.StartNew), these fields race, causing the adaptive back-off logic to be non-deterministic. Under a race, _currentInterval could bounce between extremes, either over-polling during a real outage or backing off permanently during recovery.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:33-36
- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:228-292 - _AdjustPollingInterval
- **Risk:** Medium - race condition under concurrent ProcessAsync invocation
- **Discovered by:** compound-engineering:review:security-sentinel, performance-oracle

## Proposed Solutions

### Mark _currentInterval as volatile
- **Pros**: Zero-overhead for single-assignment writes
- **Cons**: Does not protect counter increments
- **Effort**: Small
- **Risk**: Low

### Add a Lock protecting all three fields in _AdjustPollingInterval
- **Pros**: Full atomicity for compound read-modify-write
- **Cons**: Minor overhead
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add volatile to _currentInterval and add a comment documenting that ProcessAsync is designed for single-threaded sequential invocation. If concurrent calls are possible, add a lock around _AdjustPollingInterval.

## Acceptance Criteria

- [ ] _currentInterval marked volatile or protected by lock
- [ ] Comment documents sequential-invocation assumption
- [ ] Race scenario covered in documentation

## Notes

Discovered during PR #194 code review (round 2)

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
