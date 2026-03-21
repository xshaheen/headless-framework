---
status: pending
priority: p2
issue_id: "013"
tags: ["code-review","architecture","dotnet"]
dependencies: []
---

# Disambiguate PublishMessagesAsync delegate arguments at HeadlessSaveChangesRunner call sites

## Problem Statement

Both HeadlessDbContext.CoreSaveChangesAsync and HeadlessIdentityDbContext.CoreSaveChangesAsync pass 'PublishMessagesAsync' twice to HeadlessSaveChangesRunner.ExecuteAsync — once for local messages and once for distributed messages. C# resolves the correct overload via target delegate type inference, but this is fragile: adding a third overload or reordering parameters in ExecuteAsync will silently bind to the wrong method. It also makes the call sites unreadable without knowing the parameter types.

## Findings

- **Location:** src/Headless.Orm.EntityFramework/Contexts/HeadlessDbContext.cs:44-45 (CoreSaveChangesAsync)
- **Location:** src/Headless.Identity.Storage.EntityFramework/HeadlessIdentityDbContext.cs:62-63 (CoreSaveChangesAsync)
- **Discovered by:** strict-dotnet-reviewer

## Proposed Solutions

### Explicit lambda wrappers at call sites
- **Pros**: Unambiguous, type-safe, clear intent
- **Cons**: Slightly more verbose
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Replace method group arguments with explicit lambdas: (emitters, tx, ct) => PublishMessagesAsync(emitters, tx, ct) for local and (emitters, tx, ct) => PublishMessagesAsync(emitters, tx, ct) for distributed. Each lambda's emitters parameter has a distinct type that drives overload resolution unambiguously.

## Acceptance Criteria

- [ ] Both call sites use explicit lambda wrappers instead of bare method group names
- [ ] No functional behavior change
- [ ] Build succeeds without warnings

## Notes

Same issue exists for the sync Execute call sites (PublishMessages instead of PublishMessagesAsync).

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
