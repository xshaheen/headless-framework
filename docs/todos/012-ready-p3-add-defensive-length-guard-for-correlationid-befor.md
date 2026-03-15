---
status: ready
priority: p3
issue_id: "012"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Add defensive length guard for CorrelationId before MaxLength(128) column

## Problem Statement

AuditLogEntryConfiguration sets HasMaxLength(128) for CorrelationId. ICorrelationIdProvider has no length constraint. If an implementation returns a value longer than 128 chars (structured JSON, long trace context), EF attempts to store it and the DB either truncates (SQLite) or throws (SQL Server, Postgres with strict mode). A DB exception from CorrelationId length causes SaveChanges to fail for ALL entries in that batch including the entity mutations — audit overhead causes entity write failure.

## Findings

- **Location:** src/Headless.AuditLog.EntityFramework/EfAuditLogStore.cs, src/Headless.AuditLog.EntityFramework/EfAuditLog.cs
- **Discovered by:** security-sentinel

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add defensive truncation in EfAuditLogStore._AddEntries and EfAuditLog.LogAsync: `CorrelationId = entry.CorrelationId?.Length > 128 ? entry.CorrelationId[..128] : entry.CorrelationId`

## Acceptance Criteria

- [ ] CorrelationId truncated to 128 chars if longer
- [ ] Same guard applied in both EfAuditLogStore and EfAuditLog
- [ ] Test: CorrelationId longer than 128 chars does not cause SaveChanges failure

## Notes

Discovered during PR #187 code review. Activity.Current?.Id (W3C format, ~55 chars) is safe, but the abstraction allows any implementation.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-15 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
