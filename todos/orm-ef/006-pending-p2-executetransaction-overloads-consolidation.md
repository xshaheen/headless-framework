---
status: pending
priority: p2
issue_id: "006"
tags: [code-review, simplification, dotnet]
dependencies: []
---

# ExecuteTransactionAsync Overloads Could Be Consolidated

## Problem Statement

Four nearly identical overloads of `ExecuteTransactionAsync` with ~45 lines each = ~180 lines of duplicated logic. This is copy-paste programming that increases maintenance burden.

## Findings

### Location
- **File:** `src/Framework.Orm.EntityFramework/Contexts/HeadlessDbContext.cs`
- **Lines:** 209-395

### Evidence
1. `ExecuteTransactionAsync(Func<Task<bool>>)` - line 209
2. `ExecuteTransactionAsync<TArg>(Func<TArg, Task<bool>>, TArg)` - line 254
3. `ExecuteTransactionAsync<TResult>(Func<Task<(bool, TResult?)>>)` - line 300
4. `ExecuteTransactionAsync<TResult, TArg>(...)` - line 348

All share the same pattern:
```csharp
await using var transaction = await context.Database.BeginTransactionAsync(isolation);
try { ... } catch { await transaction.RollbackAsync(); throw; }
if (commit) await transaction.CommitAsync(); else await transaction.RollbackAsync();
```

## Proposed Solutions

### Option 1: Reduce to 2 overloads using closures (Recommended)
```csharp
public async Task<TResult?> ExecuteTransactionAsync<TResult>(
    Func<Task<(bool Commit, TResult? Result)>> operation,
    IsolationLevel isolation = IsolationLevel.ReadCommitted,
    CancellationToken cancellationToken = default)
{
    // Single implementation
}

// Void version
public Task ExecuteTransactionAsync(
    Func<Task<bool>> operation,
    IsolationLevel isolation = IsolationLevel.ReadCommitted,
    CancellationToken cancellationToken = default)
    => ExecuteTransactionAsync(async () => (await operation(), (object?)null), isolation, cancellationToken);
```

Callers needing state use closures:
```csharp
var myArg = "hello";
await db.ExecuteTransactionAsync(async () => (true, myArg));
```

**Pros:** ~130 LOC reduction, easier maintenance
**Cons:** Minor API change
**Effort:** Medium
**Risk:** Low

### Option 2: Extract common logic to helper
Keep overloads but extract shared logic to internal helper method.

**Pros:** Preserves API exactly
**Cons:** Still has 4 overloads, less LOC reduction
**Effort:** Small
**Risk:** Very low

## Recommended Action
<!-- To be filled during triage -->

## Technical Details

### Affected Files
- `src/Framework.Orm.EntityFramework/Contexts/HeadlessDbContext.cs`

### Affected Components
- All code using ExecuteTransactionAsync

### Database Changes Required
None

## Acceptance Criteria
- [ ] Reduced code duplication
- [ ] All existing functionality preserved
- [ ] Tests cover all execution paths

## Work Log
| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-12 | Identified during simplicity review | Closures eliminate need for TArg overloads |

## Resources
- N/A
