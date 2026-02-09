---
status: ready
priority: p2
issue_id: "008"
tags: ["data-integrity","code-review","scheduling"]
dependencies: []
---

# Guard TriggerAsync and EnableAsync against running job conflicts

## Problem Statement

TriggerAsync and EnableAsync on a job that's currently Running can cause conflicting writes — two execution paths updating the same job simultaneously without coordination.

## Findings

- **Location:** src/Headless.Messaging.Core/Scheduling/ScheduledJobManager.cs
- **Risk:** Medium - concurrent writes from trigger + active execution
- **Reviewer:** data-integrity-guardian

## Proposed Solutions

### Return error/no-op when job is Running
- **Pros**: Simple guard; prevents conflict
- **Cons**: User must retry after completion
- **Effort**: Small
- **Risk**: Low

### Queue trigger for after current execution completes
- **Pros**: Better UX
- **Cons**: More complex; needs queuing mechanism
- **Effort**: Medium
- **Risk**: Medium


## Recommended Action

Check job status before TriggerAsync/EnableAsync. Return appropriate error if Running. Let optimistic concurrency (#003) catch remaining races.

## Acceptance Criteria

- [ ] TriggerAsync returns error when job is Running
- [ ] EnableAsync handles Running state gracefully

## Notes

PR #170 code review finding. Depends on #003.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-02-09 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
