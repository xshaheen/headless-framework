---
status: pending
priority: p1
issue_id: "081"
tags: ["code-review","messaging","concurrency","correctness"]
dependencies: []
---

# Fix ResumeTask assigned after Task.Run — DisposeAsync can miss in-flight callback

## Problem Statement

_OnOpenTimerElapsed in CircuitBreakerStateManager launches a Task.Run then stores the task handle in state.ResumeTask in a separate lock. DisposeAsync reads state.ResumeTask to await it before returning. If DisposeAsync acquires the group lock between Task.Run launch and the lock that stores state.ResumeTask, it sees null and proceeds — allowing the resume callback to run after DisposeAsync returns and potentially resuming a transport that should be stopped.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:847-869
- **Race window:** Between Task.Run call and state.ResumeTask = task assignment
- **Impact:** Resume callback runs after DisposeAsync, resuming stopped transports
- **Discovered by:** strict-dotnet-reviewer, performance-oracle

## Proposed Solutions

### Use TaskCompletionSource pre-assigned before Task.Run
- **Pros**: Eliminates the race by making the task observable before it starts
- **Cons**: Slightly more code
- **Effort**: Small
- **Risk**: Low

### Assign state.ResumeTask inside the same lock that reads the state before Task.Run
- **Pros**: Structural fix matching the existing lock pattern
- **Cons**: Task.Run inside lock is acceptable only if it's fire-and-forget
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Pre-create a TaskCompletionSource and assign its Task to state.ResumeTask inside the lock before launching Task.Run. The Task.Run completes the TCS in its finally block.

## Acceptance Criteria

- [ ] state.ResumeTask is assigned before Task.Run executes
- [ ] DisposeAsync can always see and await the in-flight task
- [ ] No transport resumes after DisposeAsync returns
- [ ] Unit test: DisposeAsync during HalfOpen transition awaits the resume task

## Notes

PR #194 code review finding.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
