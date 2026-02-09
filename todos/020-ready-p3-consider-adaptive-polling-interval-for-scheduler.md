---
status: ready
priority: p3
issue_id: "020"
tags: ["performance","code-review","scheduling"]
dependencies: []
---

# Consider adaptive polling interval for scheduler

## Problem Statement

SchedulerBackgroundService polls at a fixed interval regardless of whether jobs are due. During idle periods, this wastes DB queries. During busy periods, fixed interval may be too slow.

## Findings

- **Location:** src/Headless.Messaging.Core/Scheduling/SchedulerBackgroundService.cs
- **Reviewer:** performance-oracle

## Proposed Solutions

### Exponential back-off when idle, fast poll when busy
- **Pros**: Reduces idle DB load; responsive under load
- **Cons**: More complex polling logic
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Track consecutive empty polls. Back off to longer interval (e.g., 2x, capped). Reset to base interval when jobs found.

## Acceptance Criteria

- [ ] Polling interval increases during idle periods
- [ ] Resets to base on job discovery

## Notes

PR #170 code review finding. Nice-to-have optimization.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-02-09 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
