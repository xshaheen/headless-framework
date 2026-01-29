---
status: done
priority: p2
issue_id: "014"
tags: []
dependencies: []
---

# Track consumer tasks for graceful shutdown

## Problem Statement

IConsumerRegister.Default.cs:131-171 discards consumer loop tasks with _ = Task.Factory.StartNew() and sets _compositeTask = Task.CompletedTask.

## Findings

- **Status:** Identified during workflow execution
- **Priority:** p2

## Proposed Solutions

### Option 1: [Primary solution]
- **Pros**: [Benefits]
- **Cons**: [Drawbacks]
- **Effort**: Small/Medium/Large
- **Risk**: Low/Medium/High

## Recommended Action

[To be filled during triage]

## Acceptance Criteria
- [x] Capture started tasks and wait on them during Dispose for graceful shutdown

## Notes

Source: Workflow automation

## Work Log

### 2026-01-24 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create

### 2026-01-25 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-25 - Completed

**By:** Agent
**Actions:**
- Collect consumer tasks in List<Task> instead of discarding
- Use .Unwrap() to get inner Task from Task<Task> returned by StartNew
- Set _compositeTask = Task.WhenAll(consumerTasks) for graceful shutdown
- Status changed: ready -> done

### 2026-01-29 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
