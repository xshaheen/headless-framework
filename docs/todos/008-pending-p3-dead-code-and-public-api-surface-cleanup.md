---
status: pending
priority: p3
issue_id: "008"
tags: ["code-review","quality","dotnet"]
dependencies: []
---

# Dead code and public API surface cleanup

## Problem Statement

Several dead code and visibility issues: (1) IntExtension.ToInt32OrDefault is never called — delete the entire public static class. (2) WarpResult is public but only used internally — make internal sealed. (3) await Task.CompletedTask at end of async IAsyncEnumerable methods (lines 77,121 in JobsInMemoryPersistenceProvider) — remove. (4) AuthMiddleware magic string keys on context.Items — make constants or remove if unused downstream.

## Findings

- **Dead IntExtension:** src/Headless.Messaging.Dashboard/Endpoints/MessagingDashboardEndpoints.cs:574-583
- **Public WarpResult:** src/Headless.Messaging.Dashboard/Endpoints/MessagingDashboardEndpoints.cs:556
- **Pointless await:** src/Headless.Jobs.Core/Src/Provider/JobsInMemoryPersistenceProvider.cs:77,121
- **Magic strings:** src/Headless.Dashboard.Authentication/AuthMiddleware.cs:58-59
- **Discovered by:** code-simplicity-reviewer, pragmatic-dotnet-reviewer, strict-dotnet-reviewer

## Proposed Solutions

### Delete dead code, fix visibility, remove pointless awaits
- **Pros**: Reduces public API surface, removes confusion
- **Cons**: None
- **Effort**: Small
- **Risk**: None


## Recommended Action

Delete IntExtension class. Make WarpResult internal sealed. Remove await Task.CompletedTask lines. Make context.Items keys constants or verify if used.

## Acceptance Criteria

- [ ] No dead public classes
- [ ] WarpResult is internal sealed
- [ ] No await Task.CompletedTask anti-pattern

## Notes

Source: Code review

## Work Log

### 2026-03-17 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
