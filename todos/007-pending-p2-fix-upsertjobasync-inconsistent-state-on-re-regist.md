---
status: pending
priority: p2
issue_id: "007"
tags: ["data-integrity","code-review","scheduling"]
dependencies: []
---

# Fix UpsertJobAsync inconsistent state on re-registration

## Problem Statement

When a previously-disabled job is re-registered via UpsertJobAsync, the INSERT sets IsEnabled=true but on conflict (UPDATE) only updates cron/timezone/metadata. This leaves Status=Disabled with IsEnabled=true — an inconsistent state that confuses the scheduler.

## Findings

- **Location:** src/Headless.Messaging.PostgreSql/PostgreSqlScheduledJobStorage.cs (UpsertJobAsync)
- **Risk:** Medium - re-registered disabled jobs stuck in limbo state
- **Reviewer:** data-integrity-guardian

## Proposed Solutions

### Include status reset in ON CONFLICT UPDATE
- **Pros**: Ensures clean re-registration
- **Cons**: May surprise users expecting disabled state preserved
- **Effort**: Small
- **Risk**: Low

### Only update IsEnabled if job was previously disabled by system (not user)
- **Pros**: Respects user intent
- **Cons**: Needs tracking of disable source
- **Effort**: Medium
- **Risk**: Medium


## Recommended Action

On conflict, if IsEnabled changes to true, also set Status to Idle. Document the behavior.

## Acceptance Criteria

- [ ] Re-registered disabled job transitions to consistent Idle+Enabled state
- [ ] Test covers re-registration of disabled job

## Notes

PR #170 code review finding.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
