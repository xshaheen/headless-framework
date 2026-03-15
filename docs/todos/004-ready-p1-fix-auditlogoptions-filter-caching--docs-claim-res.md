---
status: ready
priority: p1
issue_id: "004"
tags: ["code-review","performance","security","dotnet"]
dependencies: []
---

# Fix AuditLogOptions filter caching — docs claim 'result is cached' but delegates are called on every SaveChanges

## Problem Statement

AuditLogOptions.EntityFilter (line 38) says 'Called per entity type; result is cached per type' and PropertyFilter (line 45) says 'Called per property; result is cached'. Neither is true. Both delegates are called raw on every SaveChanges — EfAuditChangeCapture.cs:104 calls `opts.EntityFilter?.Invoke(clrType)` and line 154 calls `opts.PropertyFilter?.Invoke(clrType, propertyName)` — no memoization. PropertyFilter fires per-property per-entity, which on an entity with 20 properties and 50 modified entities = 1000 uncached delegate invocations per SaveChanges. Consumers who read the docs and write expensive filter predicates (regex, DB lookup, config store reads) will cause production performance incidents. Doc/code contract inversion also means a stateful filter could create a compliance bypass: if filter returns true some requests and false others, sensitive entity changes are silently dropped from the audit trail.

## Findings

- **Location:** src/Headless.AuditLog.Abstractions/AuditLogOptions.cs:38,45
- **Code location:** src/Headless.AuditLog.EntityFramework/EfAuditChangeCapture.cs:104,154
- **Severity:** P1 — documented contract violated, compliance bypass risk
- **Discovered by:** strict-dotnet-reviewer, performance-oracle, security-sentinel

## Proposed Solutions

### Option A: Implement caching (matches documented contract)
- **Pros**: Honors the docs, fixes performance, prevents stateful filter bypass
- **Cons**: Static cache — must document that EntityFilter must be a pure/stable predicate
- **Effort**: Small
- **Risk**: Low

### Option B: Remove caching claim from docs
- **Pros**: Minimal code change
- **Cons**: Leaves performance issue; consumers write expensive filters trusting the cached claim
- **Effort**: Trivial
- **Risk**: Low


## Recommended Action

Option A: Add ConcurrentDictionary<Type, bool> _EntityFilterCache and ConcurrentDictionary<(Type, string), bool> _PropertyFilterCache in EfAuditChangeCapture. Fix doc to clarify filter must be pure/deterministic (not called per-request but cached per type/property after first invocation).

## Acceptance Criteria

- [ ] EntityFilter result cached per Type after first invocation
- [ ] PropertyFilter result cached per (Type, propertyName) after first invocation
- [ ] XML docs accurately describe caching behavior
- [ ] Test: EntityFilter called exactly once per entity type across multiple SaveChanges calls

## Notes

Discovered during PR #187 code review. Both security-sentinel and performance-oracle flagged this independently.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-15 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
