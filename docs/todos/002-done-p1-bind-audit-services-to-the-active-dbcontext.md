---
status: done
priority: p1
issue_id: "002"
tags: ["code-review","architecture","dotnet","entity-framework"]
dependencies: []
---

# Bind audit services to the active DbContext

## Problem Statement

AddHeadlessDbContext<TDbContext>() now registers DbContext with TryAddScoped. The first Headless DbContext registration wins for the entire container, but EfAuditLogStore and EfAuditLog both inject plain DbContext. In any app that registers more than one context in the same scope, audit writes can target the wrong context instance or fail because the audit entity is not mapped there.

## Findings

- **Location:** src/Headless.Orm.EntityFramework/Setup.cs:55-58
- **Location:** src/Headless.AuditLog.EntityFramework/EfAuditLogStore.cs:7-25
- **Location:** src/Headless.AuditLog.EntityFramework/EfAuditLog.cs:9-33
- **Risk:** High - multi-context applications can write audit rows to the wrong database/context, especially when identity and application contexts coexist

## Proposed Solutions

### Pass the current context into the store or logger
- **Pros**: Eliminates ambiguity and guarantees the audit write uses the same context instance that is saving changes
- **Cons**: Changes the internal abstraction and SaveChanges integration points
- **Effort**: Medium
- **Risk**: Low

### Register keyed or context-specific audit services
- **Pros**: Preserves constructor injection and supports multiple contexts cleanly
- **Cons**: More DI complexity and may require newer DI features or a factory abstraction
- **Effort**: Medium
- **Risk**: Medium


## Recommended Action

Stop resolving a global DbContext from DI for audit writes; instead route the current saving context through the audit store/logger path explicitly.

## Acceptance Criteria

- [ ] Audit writes always use the same DbContext instance that is executing SaveChanges
- [ ] A scope with two registered Headless DbContexts does not cross-write audit rows
- [ ] Regression coverage added for multi-context registration

## Notes

The branch plan itself flags multi-DbContext scope as a risk, but the current implementation still relies on a single global DbContext binding.

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
