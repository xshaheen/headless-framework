---
status: pending
priority: p3
issue_id: "028"
tags: ["naming","code-review","scheduling"]
dependencies: []
---

# Rename SchedulerJobReconciler for naming consistency

## Problem Statement

SchedulerJobReconciler mixes the Scheduler* (system-level) and ScheduledJob* (entity-level) prefixes. Since it operates on ScheduledJob entities, it should be ScheduledJobReconciler for consistency with ScheduledJobDispatcher and ScheduledJobManager.

## Findings

- **Location:** src/Headless.Messaging.Core/Scheduling/SchedulerJobReconciler.cs
- **Reviewer:** pattern-recognition-specialist

## Proposed Solutions

### Rename to ScheduledJobReconciler
- **Pros**: Consistent naming pattern
- **Cons**: Internal type, low impact
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Rename class and file to ScheduledJobReconciler.

## Acceptance Criteria

- [ ] Class renamed to ScheduledJobReconciler

## Notes

PR #170 code review finding. Internal class, no public API impact.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
