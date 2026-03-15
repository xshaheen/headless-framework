---
status: done
priority: p2
issue_id: "002"
tags: ["code-review","architecture","dotnet","quality"]
dependencies: []
---

# Remove required modifier from nullable properties in AuditLogEntryData

## Problem Statement

`required string? UserId`, `required string? AccountId`, `required string? TenantId`, `required AuditChangeType? ChangeType`, `required string? EntityType`, `required string? EntityId` — using `required` on nullable types forces callers to explicitly write `UserId = null` rather than omitting the property. For a DTO only ever constructed by the framework's own capture pipeline, this adds noise at every construction site in `_CaptureEntry` with no safety benefit. It also signals a confusing API contract to downstream consumers who might want to construct `AuditLogEntryData` in tests.

## Findings

- **Location:** src/Headless.AuditLog.Abstractions/AuditLogEntryData.cs:13,16,19,35,39,42
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer

## Proposed Solutions

### Remove required from nullable fields
- **Pros**: Cleaner API, idiomatic .NET, nullable already expresses optionality
- **Cons**: None — callers aren't relying on the required constraint
- **Effort**: Trivial
- **Risk**: Low


## Recommended Action

Remove `required` from UserId, AccountId, TenantId, ChangeType, EntityType, EntityId. Keep `required` on Action (non-nullable string) and CreatedAt (DateTimeOffset).

## Acceptance Criteria

- [ ] No required modifier on nullable properties
- [ ] required retained on non-nullable Action and CreatedAt
- [ ] No behavior change — all callers already set all properties

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

### 2026-03-15 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
