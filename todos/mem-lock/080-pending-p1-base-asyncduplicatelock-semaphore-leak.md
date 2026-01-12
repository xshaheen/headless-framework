# AsyncDuplicateLock SemaphoreSlim Never Disposed

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, memory-leak, threading, dotnet

---

## Problem Statement

In `AsyncDuplicateLock.cs` (lines 42-44, 62-68), `SemaphoreSlim` instances are created but never disposed when removed from dictionary:

```csharp
// Line 42-44: SemaphoreSlim created
item = new RefCounted<SemaphoreSlim>(new SemaphoreSlim(1, 1));

// Line 62-68: When RefCount hits 0, removed but NOT disposed
if (item.RefCount == 0)
{
    _SemaphoreSlims.Remove(key);
}
item.Value.Release();  // Release called, but Dispose() never called!
```

**Why it matters:**
- `SemaphoreSlim` implements `IDisposable` and holds native resources
- Each unique key that reaches RefCount=0 leaves an undisposed semaphore
- Memory leak in long-running services with many unique keys
- ~100 bytes per leaked semaphore, unbounded growth

---

## Proposed Solutions

### Option A: Dispose After Remove
```csharp
if (item.RefCount == 0)
{
    _SemaphoreSlims.Remove(key);
    item.Value.Dispose();  // Add disposal
}
item.Value.Release();
```
- **Pros:** Simple fix, minimal change
- **Cons:** Dispose after Release is unusual ordering
- **Effort:** Small
- **Risk:** Low

### Option B: Use Nito.AsyncEx (already a dependency)
```csharp
// Replace entire class with:
private static readonly AsyncKeyedLock<string> _locks = new();
public static async Task<IDisposable> LockAsync(string key) => await _locks.LockAsync(key);
```
- **Pros:** Battle-tested, handles edge cases, simpler code
- **Cons:** API change, different disposal semantics
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option B** - Use `Nito.AsyncEx.AsyncKeyedLock<string>` which is already a dependency.

---

## Technical Details

**Affected Files:**
- `src/Framework.Base/Threading/AsyncDuplicateLock.cs` (lines 42-44, 62-68)

---

## Acceptance Criteria

- [ ] SemaphoreSlim instances are properly disposed
- [ ] No memory leak under repeated lock/unlock cycles
- [ ] Unit test verifying disposal

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer |
