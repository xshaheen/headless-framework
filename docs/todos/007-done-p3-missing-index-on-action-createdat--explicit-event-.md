---
status: done
priority: p3
issue_id: "007"
tags: ["code-review","performance","audit","database"]
dependencies: []
---

# Missing index on (Action, CreatedAt) — explicit event queries will scan

## Problem Statement

The audit log table has indexes for tenant+time, tenant+entity+time, tenant+actor+time, and correlationId. But IReadAuditLog.QueryAsync also supports filtering by `action` (e.g., 'pii.revealed', 'entity.created'), which is a primary query pattern for explicit audit events. Without an action-based index, action-filtered queries will perform a full scan or use a sub-optimal plan at scale. For compliance use cases (e.g., 'show all PII access events in the last 30 days'), this index is essential.

## Findings

- **AuditLogEntryConfiguration indexes:** src/Headless.AuditLog.EntityFramework/AuditLogEntryConfiguration.cs:68-87
- **EfReadAuditLog action filter:** src/Headless.AuditLog.EntityFramework/EfReadAuditLog.cs:13-14

## Proposed Solutions

### Add index on (Action, CreatedAt)
- **Pros**: Direct support for action-filtered time-ranged queries.
- **Cons**: Additional index write overhead.
- **Effort**: Small
- **Risk**: Low

### Add composite index on (TenantId, Action, CreatedAt) for multi-tenant action queries
- **Pros**: Better for multi-tenant SaaS usage pattern.
- **Cons**: Larger index. Adds more write overhead.
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Add HasIndex(e => new { e.TenantId, e.Action, e.CreatedAt }).HasDatabaseName('ix_audit_log_tenant_action_time') to AuditLogEntryConfiguration. This supports the primary compliance query pattern of 'what actions happened for this tenant in this time range'.

## Acceptance Criteria

- [ ] AuditLogEntryConfiguration has an index covering (TenantId, Action, CreatedAt)
- [ ] Migration is generated and applied

## Notes

Discovered during PR #187 review. Non-blocking for launch but important before high-volume production use.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-15 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-15 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
