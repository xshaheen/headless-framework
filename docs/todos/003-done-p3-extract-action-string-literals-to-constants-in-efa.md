---
status: done
priority: p3
issue_id: "003"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Extract action string literals to constants in EfAuditChangeCapture

## Problem Statement

_DetermineAction uses 8 inline magic strings: entity.soft_deleted, entity.restored, entity.suspended, entity.unsuspended, entity.created, entity.updated, entity.deleted, entity.unknown. String literals scattered across the method; typos would produce silent incorrect audit records with no compile-time detection. Tests asserting against action values must duplicate the strings.

## Findings

- **Location:** src/Headless.AuditLog.EntityFramework/EfAuditChangeCapture.cs:211-251
- **Discovered by:** code-simplicity-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Add `private static class AuditActions { public const string SoftDeleted = "entity.soft_deleted"; ... }` nested in EfAuditChangeCapture. Update tests to assert against the constants.

## Acceptance Criteria

- [x] All 8 action strings extracted to named constants
- [x] Tests use constants not inline strings

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
