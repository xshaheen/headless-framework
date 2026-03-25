---
status: pending
priority: p2
issue_id: "122"
tags: ["code-review","data-integrity","dotnet"]
dependencies: []
---

# Make SQL Server MessageId repair rerunnable after partial upgrades

## Problem Statement

SqlServerStorageInitializer only backfills and hardens the new published MessageId column when the column is missing entirely. If an upgrade is interrupted after ADD COLUMN but before the NULL backfill or NOT NULL step, later startups skip the repair and the dashboard reader can throw when it reads a NULL MessageId.

## Findings

- **Location:** src/Headless.Messaging.SqlServer/SqlServerStorageInitializer.cs:125
- **Location:** src/Headless.Messaging.SqlServer/SqlServerMonitoringApi.cs:156
- **Risk:** High - partial migrations are not self-healing and can break monitoring/list reads
- **Discovered by:** compound-engineering:review:schema-drift-detector

## Proposed Solutions

### Split add/backfill/alter into independent guarded steps
- **Pros**: Reruns safely repair partial upgrades
- **Cons**: Slightly more migration code
- **Effort**: Small
- **Risk**: Low

### Keep one-shot migration and rely on manual repair
- **Pros**: No additional code paths
- **Cons**: Operators must detect and fix broken upgrades by hand
- **Effort**: Small
- **Risk**: High


## Recommended Action

Use independent guarded steps so SQL Server can repair partially applied upgrades on rerun, then add an in-place upgrade test that seeds the partial state.

## Acceptance Criteria

- [ ] Rerunning SqlServerStorageInitializer backfills NULL MessageId values even when the column already exists
- [ ] The initializer only flips MessageId to NOT NULL after the backfill is complete
- [ ] An integration test covers the add-column-but-null-data partial-upgrade state

## Notes

PR #198 code review on 2026-03-25

## Work Log

### 2026-03-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
