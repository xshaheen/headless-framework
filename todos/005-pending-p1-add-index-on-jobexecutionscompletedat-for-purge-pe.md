---
status: pending
priority: p1
issue_id: "005"
tags: ["performance","code-review","scheduling","postgresql"]
dependencies: []
---

# Add index on job_executions.CompletedAt for purge performance

## Problem Statement

PurgeOldExecutionsAsync uses WHERE completed_at < @threshold but there is no index on CompletedAt. As the job_executions table grows, purge queries will degrade to full table scans, blocking other operations.

## Findings

- **Location:** src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs (DDL)
- **Risk:** High - purge performance degrades linearly with table size
- **Reviewer:** performance-oracle, data-integrity-guardian

## Proposed Solutions

### Add btree index on completed_at
- **Pros**: Direct fix; simple
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add CREATE INDEX IF NOT EXISTS idx_job_executions_completed_at ON job_executions(completed_at) to the DDL initializer.

## Acceptance Criteria

- [ ] Index exists on job_executions.completed_at
- [ ] Purge query uses index (verified via EXPLAIN)

## Notes

PR #170 code review finding.

## Work Log

### 2026-02-08 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
