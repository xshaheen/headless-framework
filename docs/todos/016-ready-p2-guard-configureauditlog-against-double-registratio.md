---
status: ready
priority: p2
issue_id: "016"
tags: ["code-review","architecture","dotnet"]
dependencies: []
---

# Guard ConfigureAuditLog against double-registration or document last-wins behavior

## Problem Statement

AuditLogModelBuilderExtensions.ConfigureAuditLog can be called multiple times with different tableName, schema, or jsonColumnType values. EF Core's ApplyConfiguration silently overwrites previous configuration — the last call wins. In a multi-module setup where two modules call ConfigureAuditLog with different table names, one table is silently dropped from the schema. No warning is emitted, migration generates the wrong schema.

## Findings

- **Location:** src/Headless.AuditLog.EntityFramework/AuditLogModelBuilderExtensions.cs:24-33
- **Discovered by:** security-sentinel

## Proposed Solutions

### Guard against double-registration
- **Pros**: Fails fast with a clear error instead of silently misconfiguring
- **Cons**: Breaks consumers who call it twice intentionally
- **Effort**: Trivial
- **Risk**: Low

### Document last-wins behavior
- **Pros**: Minimal code change
- **Cons**: Silent misconfiguration remains possible
- **Effort**: Trivial
- **Risk**: Low


## Recommended Action

Add guard: `if (modelBuilder.Model.FindEntityType(typeof(AuditLogEntry)) is not null) return modelBuilder;` and add XML doc saying 'call at most once per ModelBuilder'.

## Acceptance Criteria

- [ ] Double call either throws InvalidOperationException with clear message or is idempotent (second call is no-op)
- [ ] XML docs state single-call contract

## Notes

Discovered during PR #187 code review.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-15 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
