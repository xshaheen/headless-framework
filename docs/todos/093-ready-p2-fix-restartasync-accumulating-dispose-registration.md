---
status: ready
priority: p2
issue_id: "093"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix ReStartAsync accumulating Dispose registrations on successive restarts

## Problem Statement

ConsumerRegister.ReStartAsync creates a new CancellationTokenSource and registers Dispose on its token on every call. CancellationTokenSource.Dispose() does not deregister registered callbacks. On the nth restart, the nth-1 old CTS could still call Dispose prematurely if the host stopping token fires at an inopportune time. While Dispose is guarded by Interlocked.Exchange, the multiple registrations are wasteful and create a subtle ordering dependency.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs (ReStartAsync, ~line 3415-3420)
- **Risk:** Accumulated CTS registrations; potential premature Dispose call on restart
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Cancel and dispose old CTS before creating new one, deregistering callbacks
- **Pros**: Clean lifecycle, no accumulated registrations
- **Cons**: Minor refactor
- **Effort**: Small
- **Risk**: Low


## Recommended Action

In ReStartAsync: cancel-and-dispose old _stoppingCts before creating new one. CancellationTokenSource.Cancel() before Dispose() triggers callbacks (safe since Dispose is guarded), then the new CTS starts clean.

## Acceptance Criteria

- [ ] Old CTS cancelled and disposed before new CTS created in ReStartAsync
- [ ] No accumulated Dispose registrations across restarts
- [ ] Test added for multiple restart cycles

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
