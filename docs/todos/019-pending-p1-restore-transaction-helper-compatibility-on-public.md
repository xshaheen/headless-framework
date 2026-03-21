---
status: pending
priority: p1
issue_id: "019"
tags: ["code-review","dotnet","entity-framework","architecture"]
dependencies: []
---

# Restore transaction helper compatibility on public EF context types

## Problem Statement

The working tree removes the four public ExecuteTransactionAsync overloads from HeadlessDbContext and HeadlessIdentityDbContext and reintroduces them only as extension methods on DbContext. That is a public API break for downstream consumers that compile against the base-type surface, wrap these members in their own abstractions, or rely on reflection/binary compatibility. The test harness had to delete IHarnessDbContext.ExecuteTransactionAsync and the identity test adapter solely to keep compiling, which is direct evidence of the break.

## Findings

- **Location:** src/Headless.Orm.EntityFramework/Extensions/DbContextTransactionExtensions.cs:15-225
- **Evidence:** src/Headless.Orm.EntityFramework/Contexts/HeadlessDbContext.cs and src/Headless.Identity.Storage.EntityFramework/HeadlessIdentityDbContext.cs no longer expose ExecuteTransactionAsync* members
- **Compatibility signal:** tests/Headless.Orm.Tests.Harness/Fixtures/IHarnessDbContext.cs and tests/Headless.Identity.Storage.EntityFramework.Tests.Integration/Fixture/TestIdentityDbContext.cs had to drop their transaction-helper members
- **Risk:** High - source/binary breaking change in public NuGet surface

## Proposed Solutions

### Keep instance shims on the public context base classes
- **Pros**: Preserves existing API and lets the new extension implementation stay centralized
- **Cons**: Retains a small amount of forwarding code on both base classes
- **Effort**: Small
- **Risk**: Low

### Ship as an explicit breaking change with migration guidance
- **Pros**: Keeps the public surface smaller
- **Cons**: Forces downstream source changes and likely a major-version rollout
- **Effort**: Medium
- **Risk**: High


## Recommended Action

Add instance forwarding methods back to both public context base classes and keep the shared implementation in DbContextTransactionExtensions.

## Acceptance Criteria

- [ ] HeadlessDbContext exposes the previous ExecuteTransactionAsync overload set again
- [ ] HeadlessIdentityDbContext exposes the previous ExecuteTransactionAsync overload set again
- [ ] Existing callers compile without new using directives or abstraction changes
- [ ] Tests cover transaction helper behavior on both EF context flavors or the break is explicitly versioned and documented

## Notes

Discovered during 2026-03-21 dev:code-review of the current working tree on main.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
