# Missing AnyContext() Calls in Async Methods

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, async, dotnet

---

## Problem Statement

Multiple async methods are missing `.AnyContext()` calls (equivalent to `.ConfigureAwait(false)`):

**AsyncOneTimeRunner.cs (lines 24, 31):**
```csharp
using (await _semaphore.LockAsync())  // Missing AnyContext()
{
    await action();  // Missing AnyContext()
}
```

**StreamExtensions.cs (line 156):**
```csharp
await writer.WriteAsync(text);  // Missing AnyContext()
```

**NestedStream.cs (line 139):**
```csharp
await _underlyingStream.ReadAsync(...).ConfigureAwait(false);  // Uses ConfigureAwait instead of AnyContext
```

**Why it matters:**
- Per project conventions (CLAUDE.md), use `AnyContext()` for consistency
- Prevents deadlocks in UI/sync-context scenarios
- Inconsistent usage makes codebase harder to maintain

---

## Proposed Solutions

### Option A: Add AnyContext() to All Awaits
```csharp
// AsyncOneTimeRunner
await _semaphore.LockAsync().AnyContext();
await action().AnyContext();

// StreamExtensions
await writer.WriteAsync(text).AnyContext();
```
- **Pros:** Consistent with project conventions
- **Cons:** Many files to update
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option A** - Search entire codebase for `await` without `AnyContext()` and add it.

---

## Technical Details

**Affected Files:**
- `src/Framework.Base/Threading/AsyncOneTimeRunner.cs` (lines 24, 31)
- `src/Framework.Base/IO/StreamExtensions.cs` (line 156)
- `src/Framework.Base/IO/NestedStream.cs` (line 139 - change ConfigureAwait to AnyContext)

---

## Acceptance Criteria

- [ ] All `await` calls in Framework.Base use `.AnyContext()`
- [ ] No direct `.ConfigureAwait(false)` usage
- [ ] Grep for `await.*(?<!AnyContext\(\))$` returns no matches

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer |
