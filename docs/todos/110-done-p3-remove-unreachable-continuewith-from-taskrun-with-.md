---
status: done
priority: p3
issue_id: "110"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Remove unreachable ContinueWith from Task.Run with catch-all async lambda

## Problem Statement

In CircuitBreakerStateManager._OnOpenTimerElapsed, the Task.Run async lambda has a top-level try/catch (Exception) that swallows everything. No exception can escape that lambda, so the .ContinueWith(..., OnlyOnFaulted, ...) at line 565 is unreachable dead error-handling code. This creates a false impression that there is an additional error-observation path.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs:565-570
- **Risk:** Dead code — misleads readers about error handling
- **Discovered by:** compound-engineering:review:code-simplicity-reviewer

## Proposed Solutions

### Remove the .ContinueWith chain entirely
- **Pros**: -6 LOC, clarifies catch-all is the only error path
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Delete the .ContinueWith(t => logger.LogError(...), CancellationToken.None, OnlyOnFaulted, TaskScheduler.Default) chain.

## Acceptance Criteria

- [ ] ContinueWith removed
- [ ] catch-all in Task.Run body unchanged
- [ ] Tests pass

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
