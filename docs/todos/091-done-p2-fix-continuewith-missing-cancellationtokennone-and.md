---
status: done
priority: p2
issue_id: "091"
tags: ["code-review","dotnet","messaging"]
dependencies: []
---

# Fix ContinueWith missing CancellationToken.None and TaskScheduler.Default

## Problem Statement

The fire-and-forget Task.Run in CircuitBreakerStateManager._OnOpenTimerElapsed uses ContinueWith without explicit CancellationToken.None and TaskScheduler.Default. Without CancellationToken.None, a cancelled task may suppress the fault observer continuation. Without TaskScheduler.Default, behavior is ambient-scheduler-dependent. The solutions document (Pattern 6) explicitly shows the correct form.

## Findings

- **Location:** src/Headless.Messaging.Core/CircuitBreaker/CircuitBreakerStateManager.cs (~line 2638-2641)
- **Risk:** Unobserved exceptions possible; ambient scheduler dependency
- **Discovered by:** strict-dotnet-reviewer, security-sentinel

## Proposed Solutions

### Add CancellationToken.None and TaskScheduler.Default to ContinueWith call
- **Pros**: Matches documented pattern, explicit and correct
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change to .ContinueWith(t => logger.LogError(t.Exception, ...), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default).

## Acceptance Criteria

- [ ] ContinueWith uses CancellationToken.None
- [ ] ContinueWith uses TaskScheduler.Default
- [ ] Matches pattern in docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md

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

### 2026-03-21 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
