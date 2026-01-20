---
status: ready
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
- [ ] Implement IAsyncDisposable
- [ ] Dispose SemaphoreSlim in DisposeAsync
- [ ] Add finalizer warning if not disposed
- [ ] All SemaphoreSlim instances disposed
- [ ] No resource leak warnings in tests

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
