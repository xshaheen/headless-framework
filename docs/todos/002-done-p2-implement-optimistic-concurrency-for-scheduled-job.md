---
status: done
priority: p2
issue_id: "002"
tags: ["code-review","data-integrity","architecture","dotnet"]
dependencies: []
---

# Implement optimistic concurrency for scheduled jobs in PostgreSql storage

## Problem Statement

IScheduledJobStorage.UpdateJobAsync requires optimistic concurrency via ScheduledJob.Version, and EF configuration marks Version as a concurrency token, but the PostgreSql scheduled_jobs schema and storage implementation never persist or check Version. This allows lost updates (e.g., scheduler overwriting admin changes) and makes concurrency guarantees inconsistent across providers.

## Findings

- **Schema:** /Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.PostgreSql/PostgreSqlStorageInitializer.cs:110-131
- **Update without version:** /Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.PostgreSql/PostgreSqlScheduledJobStorage.cs:194-218
- **Concurrency token configured:** /Users/xshaheen/Dev/framework/headless-framework/src/Headless.Messaging.PostgreSql/Configurations/ScheduledJobConfiguration.cs:74

## Proposed Solutions

### Add Version column + conditional updates
- **Pros**: Aligns with interface contract, prevents lost updates, consistent across storage providers
- **Cons**: Requires schema change and update logic
- **Effort**: Medium
- **Risk**: Low

### Remove Version from contract
- **Pros**: Simpler storage implementation
- **Cons**: Loses concurrency protection and breaks InMemory behavior
- **Effort**: Medium
- **Risk**: Medium


## Recommended Action

Add a Version column to scheduled_jobs, read/write it in PostgreSqlScheduledJobStorage, and enforce UpdateJobAsync with WHERE Id = @Id AND Version = @Version then increment Version on success. Throw ScheduledJobConcurrencyException when 0 rows are updated.

## Acceptance Criteria

- [x] scheduled_jobs table includes Version column with default 0
- [x] PostgreSqlScheduledJobStorage reads and writes Version
- [x] UpdateJobAsync checks Version and increments on success
- [x] Concurrency mismatch results in ScheduledJobConcurrencyException

## Notes

Found during PR #176 review

## Work Log

### 2026-02-10 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-02-10 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-02-10 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
