---
<<<<<<<< HEAD:docs/todos/009-ready-p2-clean-up-orphaned-running-executions-during-stale-.md
status: ready
|||||||| 6e93708f:todos/009-pending-p2-clean-up-orphaned-running-executions-during-stale-.md
status: pending
========
status: done
>>>>>>>> refs/heads/codex/cache-perf-fixes:docs/todos/009-done-p2-clean-up-orphaned-running-executions-during-stale-.md
priority: p2
issue_id: "009"
tags: ["data-integrity","code-review","scheduling"]
dependencies: []
---

# Clean up orphaned Running executions during stale recovery

## Problem Statement

StaleJobRecoveryService releases stale jobs by resetting job status back to Idle, but the associated JobExecution record is left in Running status forever — never marked Failed or Timed Out.

## Findings

- **Location:** src/Headless.Messaging.Core/Scheduling/StaleJobRecoveryService.cs
- **Risk:** Medium - orphaned execution records accumulate, confuse dashboards
- **Reviewer:** data-integrity-guardian

## Proposed Solutions

### Mark associated execution as TimedOut during recovery
- **Pros**: Clean execution history; simple fix
- **Cons**: None significant
- **Effort**: Small
- **Risk**: Low


## Recommended Action

When releasing a stale job, also update its latest Running execution to Status=TimedOut with ErrorDetails indicating stale recovery.

## Acceptance Criteria

- [ ] Stale recovery marks orphaned execution as TimedOut
- [ ] No Running executions remain after recovery

## Notes

PR #170 code review finding.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-02-09 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
<<<<<<<< HEAD:docs/todos/009-ready-p2-clean-up-orphaned-running-executions-during-stale-.md
|||||||| 6e93708f:todos/009-pending-p2-clean-up-orphaned-running-executions-during-stale-.md
========

### 2026-02-09 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
>>>>>>>> refs/heads/codex/cache-perf-fixes:docs/todos/009-done-p2-clean-up-orphaned-running-executions-during-stale-.md
