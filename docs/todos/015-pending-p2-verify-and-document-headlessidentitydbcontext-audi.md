---
status: pending
priority: p2
issue_id: "015"
tags: ["code-review","architecture","entity-framework"]
dependencies: []
---

# Verify and document HeadlessIdentityDbContext audit-only transaction behavior change

## Problem Statement

The old HeadlessIdentityDbContext.CoreSaveChangesAsync did NOT wrap audit-only saves (no message emitters, has audit entries) in an explicit transaction — audit was persisted outside any transaction. The new HeadlessSaveChangesRunner uses requiresExplicitTransaction = auditEntries is { Count: > 0 } || ..., which now wraps ALL contexts (including Identity) in an explicit ReadCommitted transaction when audit entries exist. This is a silent behavioral change that can affect locking behavior in concurrent identity workloads (token refresh storms, concurrent login flows). Either confirm this is intentional and document it, or restore the original no-transaction behavior for Identity.

## Findings

- **Old Identity behavior:** No transaction for audit-only saves in HeadlessIdentityDbContext
- **New behavior:** src/Headless.Orm.EntityFramework/Contexts/HeadlessSaveChangesRunner.cs:45-46 — audit entries trigger explicit transaction for all contexts
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Accept and document the unified behavior
- **Pros**: More correct — audit should be atomic with entity save. Simpler shared runner.
- **Cons**: Slight locking overhead for Identity-heavy workloads
- **Effort**: Small (just add XML doc comment)
- **Risk**: Low

### Restore Identity's no-transaction behavior via a flag
- **Pros**: Preserves prior behavior exactly
- **Cons**: Adds conditional complexity to the runner
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Accept the unified behavior (it's more correct) and add an XML doc comment to HeadlessSaveChangesRunner documenting that audit-only saves are now wrapped in a transaction for all context types.

## Acceptance Criteria

- [ ] Decision documented in code (XML comment or CLAUDE.md learnings)
- [ ] If unified behavior accepted: no functional change needed, just documentation
- [ ] If restoring prior behavior: Identity context test covers audit-only save without explicit transaction

## Notes

Source: Code review

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
