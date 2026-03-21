---
status: done
priority: p2
issue_id: "034"
tags: ["code-review","dotnet","quality"]
dependencies: []
---

# Log discarded ValueTask from PauseAsync in GroupHandle.AddClient

## Problem Statement

In GroupHandle.AddClient, when a client joins while the group is paused, client.PauseAsync() is called with the result discarded: _ = client.PauseAsync(). ValueTask exceptions are swallowed silently when discarded. If PauseAsync throws, the new client continues consuming messages while the group is supposed to be paused.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs - GroupHandle.AddClient
- **Risk:** Medium - silent exception swallow could leave new client unconsumed while circuit is open
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

### Log exception via ContinueWith on faulted task
- **Pros**: Exceptions visible, fire-and-forget preserved
- **Cons**: Slightly more verbose
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Replace `_ = client.PauseAsync()` with a pattern that logs the exception: convert to Task and attach a ContinueWith(OnlyOnFaulted) that logs the error via the available logger.

## Acceptance Criteria

- [ ] PauseAsync exception in AddClient is logged
- [ ] Consumer behavior documented: client will block at pause gate even if PauseAsync fails

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
