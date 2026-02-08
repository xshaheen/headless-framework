---
status: pending
priority: p1
issue_id: "003"
tags: ["data-integrity","code-review","scheduling","postgresql"]
dependencies: []
---

# Add optimistic concurrency to UpdateJobAsync

## Problem Statement

PostgreSqlScheduledJobStorage.cs:167-202 UpdateJobAsync overwrites the entire job row without any concurrency check (no version column, no WHERE clause on expected state). Under concurrent access, a slower writer silently overwrites changes from a faster writer.

## Findings

- **Location:** src/Headless.Messaging.PostgreSql/PostgreSqlScheduledJobStorage.cs:167-202
- **Risk:** High - silent lost updates under concurrent scheduling operations
- **Reviewer:** data-integrity-guardian, strict-dotnet-reviewer

## Proposed Solutions

### Add xmin-based optimistic concurrency (PostgreSQL system column)
- **Pros**: No schema change needed; PostgreSQL-native
- **Cons**: Provider-specific
- **Effort**: Small
- **Risk**: Low

### Add explicit version/row_version column with WHERE clause
- **Pros**: Standard pattern; provider-agnostic
- **Cons**: Requires migration
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Add a version column to scheduled_jobs and include WHERE version = @expected in UpdateJobAsync. Throw concurrency exception on 0 rows affected.

## Acceptance Criteria

- [ ] UpdateJobAsync includes concurrency check
- [ ] Concurrent updates detected and reported (not silently lost)
- [ ] Test covers concurrent update scenario

## Notes

PR #170 code review finding. Related: DisableAsync on running job also affected.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
