---
status: done
priority: p1
issue_id: "001"
tags: ["code-review","dotnet","dead-code"]
dependencies: []
---

# Remove unused _disposedCts field (dead code)

## Problem Statement

The CancellationTokenSource `_disposedCts` is created and cancelled during disposal, but its token is never linked to any operation. This is dead code that wastes allocations and creates a false impression that operations respect disposal.

## Findings

- **Location:** src/Headless.Caching.Hybrid/HybridCache.cs:39
- **Cancel call:** src/Headless.Caching.Hybrid/HybridCache.cs:1085
- **Dispose call:** src/Headless.Caching.Hybrid/HybridCache.cs:1088
- **Discovered by:** strict-dotnet-reviewer, code-simplicity-reviewer, pragmatic-dotnet-reviewer

## Proposed Solutions

### Option 1: Remove the field entirely
- **Pros**: Simple, removes dead code
- **Cons**: None
- **Effort**: Small
- **Risk**: Low

### Option 2: Wire it up properly with linked tokens
- **Pros**: Operations would respect disposal
- **Cons**: More complex, may not be needed
- **Effort**: Medium
- **Risk**: Medium


## Recommended Action

Remove the field entirely unless there's a concrete need for disposal cancellation.

## Acceptance Criteria

- [ ] _disposedCts field removed
- [ ] CancelAsync and Dispose calls removed from DisposeAsync
- [ ] Tests still pass

## Notes

All three reviewers flagged this as dead code. If disposal should cancel in-flight operations, use linked tokens instead.

## Work Log

### 2026-02-04 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-02-04 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-02-04 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
