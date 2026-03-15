---
status: pending
priority: p2
issue_id: "004"
tags: ["code-review","performance","correctness","audit"]
dependencies: []
---

# EfReadAuditLog orders by Id DESC on a composite-keyed table — semantically incorrect and index-hostile

## Problem Statement

AuditLogEntry has a composite primary key (CreatedAt, Id) optimized for time-range partitioning. EfReadAuditLog.QueryAsync orders results by Id DESC only, ignoring CreatedAt. This produces semantically incorrect 'most recent first' results on databases where Id is not globally monotonic (e.g., partitioned tables with per-partition sequences, or future migration to a non-sequential ID). Even on databases where Id is globally monotonic (SQL Server IDENTITY, PostgreSQL SEQUENCE), the intent to return 'most recent entries first' should be expressed as ORDER BY CreatedAt DESC, Id DESC to remain semantically correct regardless of DB. The composite key indexes (ix_audit_log_tenant_time etc.) include CreatedAt but not Id, so ORDER BY Id DESC forces a scan or non-optimal plan for large tables.

## Findings

- **EfReadAuditLog ordering:** src/Headless.AuditLog.EntityFramework/EfReadAuditLog.cs:40
- **Composite PK definition:** src/Headless.AuditLog.EntityFramework/AuditLogEntryConfiguration.cs:21
- **Indexes defined on CreatedAt not Id:** src/Headless.AuditLog.EntityFramework/AuditLogEntryConfiguration.cs:69-87

## Proposed Solutions

### Change OrderByDescending(e => e.Id) to OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
- **Pros**: Semantically correct. Aligns with index structure. Partition-safe.
- **Cons**: Minor change.
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Change the sort to .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id). This matches the composite PK order, uses the existing time-based indexes, and is semantically correct across all DB configurations.

## Acceptance Criteria

- [ ] EfReadAuditLog sorts by CreatedAt DESC, Id DESC
- [ ] Existing integration tests still pass with new ordering

## Notes

Discovered during PR #187 review. The current ordering will work correctly on most production DB configs today (SQL Server IDENTITY, PostgreSQL SEQUENCE) but is semantically misaligned with the table design.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
