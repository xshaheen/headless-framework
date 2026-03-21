---
status: done
priority: p2
issue_id: "118"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix ConsumerRegister.Dispose() no-op — cancellation registration fires wrong method

## Problem Statement

ConsumerRegister.Dispose() sets _disposed=1 and does nothing else. The _stoppingCts.Token.Register(Dispose) at line 64 calls this when the host stops. Cancellation fires, marks disposed, but never tears down transport clients or drains tasks. Only DisposeAsync() has real cleanup. The registration is wired to the wrong method.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/IConsumerRegister.cs:92-98
- **Risk:** Transport clients not torn down on host stop — resource leak on shutdown
- **Discovered by:** compound-engineering:review:strict-dotnet-reviewer

## Proposed Solutions

### Register async callback via Token.UnsafeRegister that fires fire-and-forget DisposeAsync
- **Pros**: Non-blocking, correct async teardown
- **Cons**: Fire-and-forget
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Use Token.UnsafeRegister with a fire-and-forget Task calling DisposeAsync().

## Acceptance Criteria

- [ ] Cancellation registration triggers real transport teardown
- [ ] DisposeAsync is the single cleanup path

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

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
