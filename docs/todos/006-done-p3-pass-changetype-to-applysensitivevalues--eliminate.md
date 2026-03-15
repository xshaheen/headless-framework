---
status: done
priority: p3
issue_id: "006"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Pass changeType to _ApplySensitiveValues — eliminate redundant inner switch

## Problem Statement

_CaptureEntry computes changeType from entry.State (line 119) and verifies it is non-null before calling _ApplySensitiveValues. _ApplySensitiveValues then re-derives changeType from entry.State with a different default (` _ => AuditChangeType.Updated` vs. outer `_ => null`). The inner default arm is logically unreachable — the outer switch already returned null/exited for unknown states. Redundant computation plus a latent semantic inconsistency in the default case.

## Findings

- **Location:** src/Headless.AuditLog.EntityFramework/EfAuditChangeCapture.cs:265-271
- **Discovered by:** code-simplicity-reviewer, performance-oracle

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Pass `AuditChangeType changeType` as a parameter to `_ApplySensitiveValues` and remove the inner switch entirely.

## Acceptance Criteria

- [x] Inner switch in _ApplySensitiveValues removed
- [x] changeType passed as parameter from _CaptureEntry
- [x] All tests still pass

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
