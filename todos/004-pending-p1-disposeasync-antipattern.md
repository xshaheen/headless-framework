---
status: pending
priority: p1
issue_id: "004"
tags: [code-review, dotnet, async, performance]
dependencies: []
---

# DisposeAsync Anti-Pattern Using Task.Run

## Problem Statement

`SqlServerEntityFrameworkDbTransaction.DisposeAsync` wraps synchronous disposal in `Task.Run`, causing thread pool starvation and potential exception swallowing.

## Findings

**File:** `src/Headless.Messaging.SqlServer/SqlServerEntityFrameworkDbTransaction.cs:48-51`

```csharp
public ValueTask DisposeAsync()
{
    return new ValueTask(Task.Run(() => _transaction.Dispose()));
}
```

**Problems:**
1. **Thread pool starvation** - Wrapping sync disposal in `Task.Run` wastes a thread pool thread for blocking work
2. **Exception swallowing** - Exceptions from `_transaction.Dispose()` inside `Task.Run()` may not propagate correctly
3. **Ignores IAsyncDisposable** - The underlying transaction may support async disposal

**Similar code smell:**
- `src/Headless.Messaging.Core/Transactions/OutboxTransactionBase.cs:93-96` - `Flush()` uses sync-over-async with `GetAwaiter().GetResult()`

## Proposed Solutions

### Option 1: Direct Disposal (Recommended)

**Approach:** Call sync dispose directly, or check for IAsyncDisposable.

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
- No thread pool waste
- Proper exception propagation
- Respects async disposal if available

**Cons:**
- Minor breaking change if caller relied on async completion

**Effort:** 30 minutes

**Risk:** Low

---

### Option 2: Simple Sync Disposal

**Approach:** Just call sync Dispose, return completed ValueTask.

```csharp
public ValueTask DisposeAsync()
{
    _transaction.Dispose();
    return ValueTask.CompletedTask;
}
```

**Pros:**
- Simplest solution
- No allocations

**Cons:**
- Blocks caller if dispose is slow

**Effort:** 15 minutes

**Risk:** Low

## Recommended Action

Implement Option 1. Check if `IOutboxTransaction` implements `IAsyncDisposable` - if so, use it. Otherwise fall back to sync disposal without `Task.Run`.

## Technical Details

**Affected files:**
- `src/Headless.Messaging.SqlServer/SqlServerEntityFrameworkDbTransaction.cs:48-51`

**Related issue:**
- `OutboxTransactionBase.Flush()` sync-over-async pattern in Core project

## Acceptance Criteria

- [ ] DisposeAsync does not use Task.Run
- [ ] Async disposal is used if available
- [ ] Exceptions propagate correctly
- [ ] Build passes

## Work Log

### 2026-01-22 - Initial Discovery

**By:** Strict .NET Reviewer Agent

**Actions:**
- Identified Task.Run anti-pattern in DisposeAsync
- Found related sync-over-async in OutboxTransactionBase
- Documented correct disposal patterns
