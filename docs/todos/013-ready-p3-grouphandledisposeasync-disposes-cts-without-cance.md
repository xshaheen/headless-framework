---
status: ready
priority: p3
issue_id: "013"
tags: ["code-review","messaging","dotnet","quality"]
dependencies: []
---

# GroupHandle.DisposeAsync disposes CTS without canceling first

## Problem Statement

GroupHandle.DisposeAsync calls Cts.Dispose() without a prior CancelAsync(). Tasks waiting on the token's WaitHandle receive ObjectDisposedException instead of clean OperationCanceledException. PulseAsync correctly cancels before disposing, but the DisposeAsync path does not.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:GroupHandle.DisposeAsync
- **Discovered by:** architecture-strategist

## Proposed Solutions

### Add await Cts.CancelAsync() before Cts.Dispose() in DisposeAsync
- **Pros**: Clean cancellation semantics
- **Cons**: None
- **Effort**: Tiny
- **Risk**: Low


## Recommended Action

Add await Cts.CancelAsync(); before Cts.Dispose(); in GroupHandle.DisposeAsync.

## Acceptance Criteria

- [ ] Consumer tasks receive OperationCanceledException not ObjectDisposedException on dispose

## Notes

PR #194 review.

## Work Log

### 2026-03-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
