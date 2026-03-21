---
status: pending
priority: p2
issue_id: "012"
tags: ["code-review","architecture","entity-framework"]
dependencies: []
---

# Replace _GetServiceOrDefault try/catch with GetInfrastructure().GetService<T>()

## Problem Statement

AuditSavePipelineHelper._GetServiceOrDefault<T> uses try/catch on EF's GetService<T>() extension (which calls GetRequiredService and throws) as a way to probe for optional service registration. This swallows ALL InvalidOperationException including EF concurrency violations (e.g. 'A second operation was started on this context before a previous completed'), producing silent null instead of a crash. The correct fix is IServiceProvider.GetService<T>() (non-throwing) via context.GetInfrastructure().GetService<T>() which returns null for unregistered services without any exception.

## Findings

- **Location:** src/Headless.Orm.EntityFramework/AuditSavePipelineHelper.cs:103-114
- **Also affected:** Lines 29-32 still call context.GetService<ICurrentUser/ICurrentTenant/etc.> directly without any guard
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer, code-simplicity-reviewer

## Proposed Solutions

### Use context.GetInfrastructure().GetService<T>()
- **Pros**: Correct, non-throwing, removes try/catch anti-pattern, consistent with IServiceProvider contract
- **Cons**: Requires adding Microsoft.EntityFrameworkCore.Infrastructure using
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Replace all _GetServiceOrDefault<T>(context) call sites with context.GetInfrastructure().GetService<T>() and delete the helper method. Also standardize lines 29-32 in CaptureAuditEntries to use the same pattern.

## Acceptance Criteria

- [ ] _GetServiceOrDefault method deleted
- [ ] All optional service resolutions use context.GetInfrastructure().GetService<T>()
- [ ] InvalidOperationException from EF lifecycle violations propagates correctly (not swallowed)
- [ ] Existing audit integration tests still pass

## Notes

The fix was attempted earlier using IInfrastructure<IServiceProvider> directly but broke integration tests — likely because GetInfrastructure() returns EF's scoped provider that includes app services. Verify this works correctly before merging.

## Work Log

### 2026-03-21 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
