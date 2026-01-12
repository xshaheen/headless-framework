---
status: pending
priority: p1
issue_id: "001"
tags: [code-review, performance, dotnet, entity-framework, async]
dependencies: []
---

# Missing CancellationToken in ExecuteTransactionAsync

## Problem Statement

The four `ExecuteTransactionAsync` overloads in `HeadlessDbContext` do not accept or propagate `CancellationToken`. This is a critical omission for any async operation in .NET.

Every async method should accept `CancellationToken cancellationToken = default` and pass it through the entire call chain. The `BeginTransactionAsync`, `SaveChangesAsync`, `RollbackAsync`, and `CommitAsync` calls inside these methods all support cancellation but receive none.

**Why it matters:** Users cannot cancel long-running transaction operations. Under load or timeout scenarios, resources remain locked and the operation cannot be gracefully terminated.

## Findings

### Location
- **File:** `src/Framework.Orm.EntityFramework/Contexts/HeadlessDbContext.cs`
- **Lines:** 209-252, 254-298, 300-346, 348-395

### Evidence
```csharp
// Line 209-212 - Missing CancellationToken parameter
public Task ExecuteTransactionAsync(
    Func<Task<bool>> operation,
    IsolationLevel isolation = IsolationLevel.ReadCommitted
)
```

All four overloads:
1. `ExecuteTransactionAsync(Func<Task<bool>>)` - line 209
2. `ExecuteTransactionAsync<TArg>(Func<TArg, Task<bool>>, TArg)` - line 254
3. `ExecuteTransactionAsync<TResult>(Func<Task<(bool, TResult?)>>)` - line 300
4. `ExecuteTransactionAsync<TResult, TArg>(...)` - line 348

## Proposed Solutions

### Option 1: Add CancellationToken to all overloads (Recommended)
Add `CancellationToken cancellationToken = default` parameter and propagate through:
- `BeginTransactionAsync(isolation, cancellationToken)`
- `SaveChangesAsync(cancellationToken)`
- `RollbackAsync(cancellationToken)`
- `CommitAsync(cancellationToken)`

**Pros:** Complete cancellation support
**Cons:** API change (but backward compatible with default)
**Effort:** Small
**Risk:** Low

### Option 2: Add new overloads with CancellationToken
Create parallel overloads that accept CancellationToken, keep old ones for compatibility.

**Pros:** No breaking change
**Cons:** Doubles the number of overloads (8 total)
**Effort:** Medium
**Risk:** Low

## Recommended Action
<!-- To be filled during triage -->

## Technical Details

### Affected Files
- `src/Framework.Orm.EntityFramework/Contexts/HeadlessDbContext.cs`

### Affected Components
- Transaction execution
- All derived DbContext classes using ExecuteTransactionAsync

### Database Changes Required
None

## Acceptance Criteria
- [ ] All ExecuteTransactionAsync overloads accept CancellationToken
- [ ] CancellationToken propagated to all async calls inside
- [ ] Tests verify cancellation behavior
- [ ] No breaking changes to existing callers

## Work Log
| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-12 | Identified during code review | All async methods should support cancellation |

## Resources
- PR: N/A (code review of existing implementation)
- Related: Microsoft async best practices
