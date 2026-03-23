---
status: ready
priority: p2
issue_id: "109"
tags: ["code-review","dotnet","thread-safety"]
dependencies: []
---

# Volatile non-atomic increments on _consecutiveHealthyCycles/_consecutiveCleanCycles

## Problem Statement

In MessageNeedToRetryProcessor, _consecutiveHealthyCycles and _consecutiveCleanCycles use volatile for reads/writes but ++ is not atomic. A concurrent ResetBackpressureAsync writing zero can race with the increment, producing a stale count after reset.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:295-342
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Replace volatile with Interlocked.Increment/Exchange — consistent with _currentIntervalTicks which already uses Interlocked.

## Acceptance Criteria

- [ ] _consecutiveHealthyCycles uses Interlocked operations
- [ ] _consecutiveCleanCycles uses Interlocked operations
- [ ] volatile keyword removed from both fields

## Notes

The current code acknowledges this as 'benign approximation' but Interlocked is trivial to adopt and consistent with the rest of the file.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
