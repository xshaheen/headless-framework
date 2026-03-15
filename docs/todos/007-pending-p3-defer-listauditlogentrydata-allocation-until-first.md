---
status: pending
priority: p3
issue_id: "007"
tags: ["code-review","performance","dotnet"]
dependencies: []
---

# Defer List<AuditLogEntryData> allocation until first auditable entity confirmed

## Problem Statement

`new List<AuditLogEntryData>()` is allocated on every CaptureChanges call before any entity is examined. When all entities are filtered out (common case: SaveChanges on non-audited entities), the allocation is wasted entirely. On high-throughput services with 100+ saves/sec this contributes unnecessary gen-0 GC pressure.

## Findings

- **Location:** src/Headless.AuditLog.EntityFramework/EfAuditChangeCapture.cs:44
- **Discovered by:** performance-oracle

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Use `List<AuditLogEntryData>? result = null;` and `result ??= new List<AuditLogEntryData>();` on first add. Return `result ?? []` at the end.

## Acceptance Criteria

- [ ] No List allocation when no auditable entities change
- [ ] Behavior identical for callers (returns empty IReadOnlyList)

## Notes

Discovered during PR #187 code review.

## Work Log

### 2026-03-15 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
