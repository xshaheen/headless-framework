---
status: done
priority: p3
issue_id: "015"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Consider renaming AuditAllEntities to clarify opt-out semantics

## Problem Statement

AuditLogOptions.AuditAllEntities = true enables opt-out mode (all entities audited unless marked [AuditIgnore]). The name sounds like 'audit everything' which conflicts with the default false value — a reader sees `AuditAllEntities = false` and thinks 'we are not auditing all entities' which is technically true but means 'we use opt-in mode'. Alternative names: `OptOutMode`, `AuditUnlessIgnored`, `UseOptOutMode`. The PR description correctly explains the semantics but the property name requires a doc lookup to understand.

## Findings

- **Location:** src/Headless.AuditLog.Abstractions/AuditLogOptions.cs:19
- **Discovered by:** pragmatic-dotnet-reviewer

## Proposed Solutions

_To be analyzed during triage._

## Recommended Action

Rename to `AuditByDefault` (true = audit all entities by default, use [AuditIgnore] to opt out) or `UseOptOutMode`. This is a breaking API change — only feasible before GA.

## Acceptance Criteria

- [x] Property renamed to a name that reads naturally in both true and false states
- [x] All usages updated
- [x] README and XML docs updated

## Notes

Discovered during PR #187 code review. P3 because naming is a DX concern, not correctness.

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
