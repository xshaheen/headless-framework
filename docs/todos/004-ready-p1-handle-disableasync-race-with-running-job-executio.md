---
status: ready
priority: p1
issue_id: "004"
tags: ["data-integrity","code-review","scheduling"]
dependencies: []
---

# Handle DisableAsync race with running job execution

## Problem Statement

When DisableAsync is called on a job that's currently running, the status is set to Disabled. But when the execution completes, SchedulerBackgroundService overwrites the status back to Running/Idle, silently undoing the disable. The user believes the job is disabled but it continues to execute.

## Findings

- **Location:** src/Headless.Messaging.Core/Scheduling/ScheduledJobManager.cs + SchedulerBackgroundService.cs
- **Risk:** High - user action (disable) silently reversed by system
- **Reviewer:** data-integrity-guardian

## Proposed Solutions

### Check IsEnabled before updating status on execution completion
- **Pros**: Simple; prevents overwrite
- **Cons**: Race window still exists
- **Effort**: Small
- **Risk**: Low

### Use atomic status transitions (CAS-style) with allowed state machine
- **Pros**: Bulletproof; no race conditions
- **Cons**: More complex SQL
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

On execution completion, use conditional update: SET status = X WHERE status != 'Disabled' AND is_enabled = true. This preserves admin intent.

## Acceptance Criteria

- [ ] DisableAsync on running job persists after execution completes
- [ ] Test covers disable-during-execution scenario

## Notes

PR #170 code review finding. Depends on #003 (optimistic concurrency).

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-02-08 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
