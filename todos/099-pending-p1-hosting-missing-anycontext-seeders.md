# Missing AnyContext() on Async Operations in DbSeedersExtensions

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, async, dotnet, hosting

---

## Problem Statement

`DbSeedersExtensions.cs` has async operations without `AnyContext()` (the project's `ConfigureAwait(false)` wrapper). Per CLAUDE.md conventions, all async operations in library code must use `AnyContext()`.

**Affected lines:**
- Line 30: `await using var scope = services.CreateAsyncScope();`
- Line 49: `await x.Seeder.SeedAsync(cancellationToken)`
- Line 57: `await seeder.SeedAsync(cancellationToken)`
- Line 66: `await using var scope = services.CreateAsyncScope();`
- Line 85: `await x.Seeder.SeedAsync(cancellationToken)`
- Line 93: `await seeder.SeedAsync(cancellationToken)`

**Why it matters:**
- Without `ConfigureAwait(false)`, if called from UI context (WPF, WinForms), risks deadlocks
- Continuation tries to marshal back to captured `SynchronizationContext`
- Inconsistent with rest of codebase

---

## Proposed Solutions

### Option A: Add AnyContext() to All Await Statements
```csharp
await using var scope = services.CreateAsyncScope().AnyContext();
// ...
await seeder.SeedAsync(cancellationToken).AnyContext();
```
- **Pros:** Follows project conventions, consistent
- **Cons:** None
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Add `AnyContext()` to all await statements.

---

## Technical Details

**Affected Files:**
- `src/Framework.Hosting/Seeders/DbSeedersExtensions.cs` (lines 30, 49, 57, 66, 85, 93)

---

## Acceptance Criteria

- [ ] All `await` statements use `.AnyContext()`
- [ ] Build passes
- [ ] Existing tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer, pattern-recognition-specialist |
