---
status: pending
priority: p3
issue_id: "025"
tags: ["yagni","code-review","scheduling"]
dependencies: []
---

# Remove unused ScheduledTrigger.ParentJobId property

## Problem Statement

ScheduledTrigger.ParentJobId is always set to null in ScheduledJobDispatcher.cs:83. No job chaining mechanism exists. This is a YAGNI violation — speculative support for a feature that doesn't exist.

## Findings

- **Location:** src/Headless.Messaging.Abstractions/ScheduledTrigger.cs:63
- **Reviewer:** code-simplicity-reviewer

## Proposed Solutions

### Remove ParentJobId property
- **Pros**: Cleaner public API; YAGNI
- **Cons**: Re-add if chaining is needed later
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Remove ParentJobId from ScheduledTrigger.

## Acceptance Criteria

- [ ] ParentJobId removed from ScheduledTrigger

## Notes

PR #170 code review finding.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
