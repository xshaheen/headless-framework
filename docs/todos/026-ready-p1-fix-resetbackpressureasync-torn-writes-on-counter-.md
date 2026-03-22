---
status: ready
priority: p1
issue_id: "026"
tags: ["code-review","threading","correctness"]
dependencies: []
---

# Fix ResetBackpressureAsync torn writes on counter fields

## Problem Statement

ResetBackpressureAsync (IProcessor.NeedRetry.cs:82-87) performs plain writes to _consecutiveHealthyCycles and _consecutiveCleanCycles. These fields are documented as 'only accessed from ProcessAsync (sequential)', but ResetBackpressureAsync is a public API on IRetryProcessorMonitor callable from any thread — operator tools, health endpoints, AI agents. A concurrent call during ProcessAsync creates a data race.

## Findings

- **Location:** src/Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs:82-87
- **Risk:** High — data race between ResetBackpressureAsync and ProcessAsync
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

### Use volatile on counter fields
- **Pros**: Minimal change, correct for single-writer-multiple-reader
- **Cons**: volatile int does not prevent compound read-modify-write races
- **Effort**: Small
- **Risk**: Low

### Use Interlocked for all counter access
- **Pros**: Fully thread-safe, consistent with _currentIntervalTicks pattern
- **Cons**: Slightly more verbose
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add volatile to _consecutiveHealthyCycles and _consecutiveCleanCycles declarations. Update the threading comment to reflect that ResetBackpressureAsync is a cross-thread writer.

## Acceptance Criteria

- [ ] Counter fields declared as volatile int
- [ ] Threading contract comment updated to document ResetBackpressureAsync as cross-thread
- [ ] No plain read/write of these fields without appropriate visibility guarantee

## Notes

Discovered by pragmatic-dotnet-reviewer. The field comment at line 37-41 is incorrect — fix it.

## Work Log

### 2026-03-22 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-22 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
