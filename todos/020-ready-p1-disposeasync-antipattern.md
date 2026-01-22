---
status: pending
priority: p1
issue_id: "020"
tags: [code-review, dotnet, async, antipattern]
dependencies: []
---

# DisposeAsync Anti-Pattern in PostgreSqlEntityFrameworkDbTransaction

## Problem Statement

The `DisposeAsync` method wraps synchronous `Dispose()` in `Task.Run()`, which is wasteful, defeats the purpose of async disposal, and can swallow exceptions.

**Impact:** Thread pool abuse, potential exception swallowing, improper resource cleanup.

## Findings

- **File:** `src/Headless.Messaging.PostgreSql/PostgreSqlEntityFrameworkDbTransaction.cs:48-51`

```csharp
public ValueTask DisposeAsync()
{
    return new ValueTask(Task.Run(() => _transaction.Dispose()));
}
```

**Issues:**
1. `Task.Run()` schedules work on thread pool unnecessarily
2. The underlying `_transaction` likely has a proper `DisposeAsync()` that should be called
3. If `_transaction.Dispose()` throws, the exception handling is incorrect
4. This pattern exists because `OutboxTransactionBase` doesn't implement `IAsyncDisposable`

**Related:** `OutboxTransactionBase` (line 133-137) only has sync `Dispose()`:
```csharp
public virtual void Dispose()
{
    (DbTransaction as IDisposable)?.Dispose();
    DbTransaction = null;
}
```

## Proposed Solutions

### Option 1: Proper Async Disposal (Recommended)

**Approach:** Implement proper async disposal checking for `IAsyncDisposable`.

```csharp
public async ValueTask DisposeAsync()
{
    if (_transaction is IAsyncDisposable asyncDisposable)
        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
    else
        _transaction.Dispose();
}
```

**Pros:**
- Proper async disposal
- No thread pool abuse
- Correct exception handling

**Cons:**
- Need to ensure IOutboxTransaction can be async disposed

**Effort:** 1 hour

**Risk:** Low

---

### Option 2: Add IAsyncDisposable to OutboxTransactionBase

**Approach:** Update the base class to support async disposal properly.

```csharp
// In OutboxTransactionBase
public virtual async ValueTask DisposeAsync()
{
    if (DbTransaction is IAsyncDisposable asyncDisposable)
        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
    else
        (DbTransaction as IDisposable)?.Dispose();
    DbTransaction = null;
}
```

**Pros:**
- Fixes the root cause
- All derived classes benefit

**Cons:**
- Breaking change to base class
- Need to update all derived classes

**Effort:** 2-3 hours

**Risk:** Medium

## Recommended Action

Implement Option 2 to fix the root cause in `OutboxTransactionBase`. This ensures all transaction types properly support async disposal.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.PostgreSql/PostgreSqlEntityFrameworkDbTransaction.cs:48-51`
- `src/Headless.Messaging.Core/Transactions/OutboxTransactionBase.cs:133-137`
- `src/Headless.Messaging.SqlServer/SqlServerEntityFrameworkDbTransaction.cs` (likely same issue)

**Related components:**
- All outbox transaction implementations
- Entity Framework transaction wrapping

## Acceptance Criteria

- [ ] No Task.Run() wrapping sync Dispose
- [ ] DisposeAsync properly awaits if IAsyncDisposable
- [ ] OutboxTransactionBase implements IAsyncDisposable
- [ ] Tests pass with `await using` pattern
- [ ] No thread pool scheduling for disposal

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Claude Code - Code Review

**Actions:**
- Identified Task.Run anti-pattern in DisposeAsync
- Traced root cause to OutboxTransactionBase lacking IAsyncDisposable
- Found similar pattern in SqlServer implementation

**Learnings:**
- Base class should implement IAsyncDisposable for proper async disposal chain
- DbTransaction implements IAsyncDisposable in modern .NET
