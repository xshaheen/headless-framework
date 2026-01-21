---
status: completed
priority: p1
issue_id: "031"
tags: []
dependencies: []
---

# semaphoreslim-dispose

## Problem Statement

IConnectionPool.Default and ScheduledMediumMessageQueue create SemaphoreSlim instances but never dispose them, causing resource leaks in long-running processes.

## Findings

- **Status:** Identified during workflow execution
- **Priority:** p1

## Proposed Solutions

### Option 1: [Primary solution]
- **Pros**: [Benefits]
- **Cons**: [Drawbacks]
- **Effort**: Small/Medium/Large
- **Risk**: Low/Medium/High

## Recommended Action

[To be filled during triage]

## Acceptance Criteria
- [x] Implement IDisposable (not IAsyncDisposable - synchronous disposal sufficient)
- [x] Dispose SemaphoreSlim in Dispose
- [x] Add finalizer warning if not disposed
- [x] All SemaphoreSlim instances disposed
- [x] No resource leak warnings in tests

## Notes

Source: Workflow automation

## Work Log

### 2026-01-20 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create

### 2026-01-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-21 - Resolved

**By:** Claude Code
**Actions:**
- Status changed: ready → completed
- Fixed RedisConnectionPool to dispose _poolLock SemaphoreSlim
- Implemented IDisposable on ScheduledMediumMessageQueue
- Added finalizers to both classes with Debug.Fail warnings
- Updated Dispatcher to dispose _schedulerQueue instance
