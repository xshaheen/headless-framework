---
status: pending
priority: p1
issue_id: "121"
tags: ["code-review","architecture","dotnet"]
dependencies: []
---

# Make published MessageId migration safe for rollout and rollback

## Problem Statement

The new published-table MessageId column is required immediately in both durable providers, so pre-refactor binaries cannot keep publishing after the schema flip and a code-only rollback is not safe once the database has been migrated. This turns the change into a full writer cutover with no compatibility guard or documented deployment gate.

## Findings

- **Location:** src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs:125
- **Location:** src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs:97
- **Location:** src/Headless.Messaging.SqlServer/SqlServerDataStorage.cs:179
- **Location:** src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs:196
- **Risk:** Critical - rolling deploys and code-only rollback can fail publishes during migration
- **Discovered by:** compound-engineering:review:deployment-verification-agent

## Proposed Solutions

### Phase the migration for backward compatibility
- **Pros**: Supports rolling deploys and safer rollback
- **Cons**: Adds an extra compatibility phase and cleanup work
- **Effort**: Medium
- **Risk**: Low

### Treat it as a hard cutover with explicit deployment gates
- **Pros**: Minimal code change
- **Cons**: Requires downtime/full writer coordination and careful rollback handling
- **Effort**: Small
- **Risk**: Medium


## Recommended Action

Either introduce a backward-compatible migration phase or explicitly gate deployment as a full writer cutover; the current in-between state is not safe for rolling rollout.

## Acceptance Criteria

- [ ] Deployment strategy is explicit: backward-compatible phased rollout or enforced full cutover
- [ ] Old writers cannot silently fail against the migrated schema
- [ ] Rollback expectations are documented and verified for SQL Server and PostgreSQL

## Notes

PR #198 code review on 2026-03-25

## Work Log

### 2026-03-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
