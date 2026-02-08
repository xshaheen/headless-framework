---
status: pending
priority: p3
issue_id: "026"
tags: ["maintainability","code-review","scheduling","postgresql"]
dependencies: []
---

# Extract duplicated PostgreSQL column lists into constants

## Problem Statement

The same 21-column SELECT list is duplicated 3 times in PostgreSqlScheduledJobStorage (AcquireDueJobsAsync, GetJobByNameAsync, GetAllJobsAsync). Column reordering or additions require updating all 3+ locations.

## Findings

- **Location:** src/Headless.Messaging.PostgreSql/PostgreSqlScheduledJobStorage.cs
- **Reviewer:** code-simplicity-reviewer

## Proposed Solutions

### Extract to private const string _JobColumns
- **Pros**: Single point of change; ~30 LOC saved
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Extract column list into a const string field. Same for execution columns.

## Acceptance Criteria

- [ ] No duplicated column lists in storage class

## Notes

PR #170 code review finding.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
