---
status: ready
priority: p2
issue_id: "099"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix Interlocked and plain-int counter mix in retry processor (ambiguous threading contract)

## Problem Statement

In IProcessor.NeedRetry._AdjustPollingInterval, _currentIntervalTicks uses Interlocked.Read/Exchange for cross-thread visibility (read from _GetLockTtl on another thread), but _consecutiveHealthyCycles and _consecutiveCleanCycles are plain int fields with no synchronization. If _AdjustPollingInterval is truly sequential, Interlocked is unnecessary. If it can be concurrent, the plain int counters are unsafe. The mixed approach is confusing and the comment acknowledges the tension without resolving it.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs (~line 242-313)
- **Risk:** Either unnecessary overhead or unsafe unsynchronized counters — ambiguous contract
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

### Document clearly that _AdjustPollingInterval is single-threaded, make _currentIntervalTicks volatile instead of Interlocked
- **Pros**: Simpler, documents the contract
- **Cons**: None if contract is actually single-threaded
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add a comment documenting the threading contract: which methods are single-threaded vs which can be called concurrently. If sequential, use volatile instead of Interlocked for _currentIntervalTicks to clarify intent.

## Acceptance Criteria

- [ ] Clear comment documents threading contract for all counter fields
- [ ] Interlocked vs volatile usage is consistent with the documented contract
- [ ] No hidden concurrency assumption

## Notes

PR #194.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-21 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
