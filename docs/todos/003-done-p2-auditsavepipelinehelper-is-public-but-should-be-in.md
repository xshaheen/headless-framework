---
status: done
priority: p2
issue_id: "003"
tags: ["code-review","api-design","framework","audit"]
dependencies: []
---

# AuditSavePipelineHelper is public but should be internal — leaks implementation detail

## Problem Statement

AuditSavePipelineHelper in Headless.Orm.EntityFramework is declared public. This exposes framework-internal save pipeline mechanics as a public API surface, creating a forward-compatibility burden. Downstream consumers could call CaptureAuditEntries, SaveAuditEntries, ResolveEntityIds directly, bypassing the intended pipeline flow or creating hidden dependencies. For a framework that emphasizes zero lock-in, an unintended public API is a maintenance liability. The helper was introduced to avoid code duplication between HeadlessDbContext and HeadlessIdentityDbContext — an internal concern.

## Findings

- **AuditSavePipelineHelper declaration:** src/Headless.Orm.EntityFramework/AuditSavePipelineHelper.cs:16
- **Called from HeadlessDbContext:** src/Headless.Orm.EntityFramework/Contexts/HeadlessDbContext.cs:43
- **Called from HeadlessIdentityDbContext:** src/Headless.Identity.Storage.EntityFramework/HeadlessIdentityDbContext.cs:61

## Proposed Solutions

### Make AuditSavePipelineHelper internal
- **Pros**: Removes unintentional public API. No behavioral change. Simple fix.
- **Cons**: HeadlessIdentityDbContext is in a different assembly — requires InternalsVisibleTo or refactoring.
- **Effort**: Small
- **Risk**: Low

### Move _ResolveAndPersistAudit logic into AuditSavePipelineHelper as static methods taking a Func<_BaseSaveChanges>
- **Pros**: Eliminates duplication in HeadlessDbContext and HeadlessIdentityDbContext. Can stay internal.
- **Cons**: More refactoring. Func delegation adds indirection.
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Change AuditSavePipelineHelper to internal. Add InternalsVisibleTo for Headless.Identity.Storage.EntityFramework in Headless.Orm.EntityFramework.csproj. This is a straightforward change with no behavioral impact.

## Acceptance Criteria

- [ ] AuditSavePipelineHelper is not part of the public API surface (internal or internal via InternalsVisibleTo)
- [ ] HeadlessIdentityDbContext can still access the helper
- [ ] No public API break for downstream consumers

## Notes

Discovered during PR #187 review. The [PublicAPI] attribute should NOT be added to AuditSavePipelineHelper.

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
