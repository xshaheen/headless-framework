---
status: pending
priority: p3
issue_id: "018"
tags: ["code-review","dotnet","entity-framework","quality","documentation"]
dependencies: []
---

# Document the new DbContext transaction extensions

## Problem Statement

DbContextTransactionExtensions adds four new public extension methods, but the methods themselves have no XML docs and the package docs do not explain that transaction helpers moved off the public context base classes. In this repo, public APIs are expected to carry XML docs and package README updates when the surface changes.

## Findings

- **Location:** src/Headless.Orm.EntityFramework/Extensions/DbContextTransactionExtensions.cs:15-225
- **Docs search:** No matching ExecuteTransactionAsync guidance in src/Headless.Orm.EntityFramework/README.md or src/Headless.Identity.Storage.EntityFramework/README.md
- **Risk:** Low - consumer confusion and undocumented public API

## Proposed Solutions

### Add XML docs and package README guidance
- **Pros**: Matches repo conventions and gives consumers a migration path
- **Cons**: Requires doc maintenance
- **Effort**: Small
- **Risk**: Low

### Hide the extension surface until documentation is ready
- **Pros**: Avoids publishing an undocumented API
- **Cons**: Blocks the refactor from landing as-is
- **Effort**: Small
- **Risk**: Medium


## Recommended Action

Add method-level XML docs and update the relevant package README or migration notes alongside the refactor.

## Acceptance Criteria

- [ ] Each public ExecuteTransactionAsync extension overload has XML docs
- [ ] Relevant package docs mention how to use the transaction helpers after the refactor
- [ ] If the move is intended as a migration, the docs call that out explicitly

## Notes

Discovered during 2026-03-21 dev:code-review of the current working tree on main.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
