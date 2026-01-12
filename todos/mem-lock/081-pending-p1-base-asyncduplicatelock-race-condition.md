# AsyncDuplicateLock Race Condition

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, threading, race-condition, dotnet

---

## Problem Statement

In `AsyncDuplicateLock.cs` (lines 57-68), `Release()` is called outside the lock, creating a race condition:

```csharp
public void Dispose()
{
    RefCounted<SemaphoreSlim> item;
    lock (_SemaphoreSlims)
    {
        item = _SemaphoreSlims[key];  // Line 59
        --item.RefCount;               // Line 60
        if (item.RefCount == 0)
        {
            _SemaphoreSlims.Remove(key);
        }
    }
    item.Value.Release();  // Line 68 - OUTSIDE the lock!
}
```

**Race scenario:**
1. Thread A enters `Dispose()`, decrements RefCount to 0, removes key, exits lock
2. Thread B calls `_GetOrCreate(key)`, creates NEW semaphore for same key
3. Thread B calls `Wait()` on NEW semaphore and blocks
4. Thread A calls `Release()` on OLD semaphore - Thread B never wakes up

**Why it matters:**
- Lock bypass could cause deadlocks
- Data corruption in code relying on this synchronization
- Security bypass if used for access control

---

## Proposed Solutions

### Option A: Move Release Inside Lock
```csharp
lock (_SemaphoreSlims)
{
    item = _SemaphoreSlims[key];
    --item.RefCount;
    if (item.RefCount == 0)
    {
        _SemaphoreSlims.Remove(key);
    }
    item.Value.Release();  // Inside lock now
}
```
- **Pros:** Simple fix
- **Cons:** Slightly longer lock hold time
- **Effort:** Small
- **Risk:** Low

### Option B: Replace with Nito.AsyncEx
- See related todo #080
- **Pros:** Eliminates custom implementation entirely
- **Cons:** API change
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option B** - Replace with Nito.AsyncEx (addresses both this and the disposal issue).

---

## Technical Details

**Affected Files:**
- `src/Framework.Base/Threading/AsyncDuplicateLock.cs` (lines 57-68)

---

## Acceptance Criteria

- [ ] No race condition between lock release and dictionary cleanup
- [ ] Concurrent stress test passes without deadlocks
- [ ] Thread B always wakes up when Thread A releases

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer, security-sentinel |
