# Inconsistent Error Handling Patterns Across Methods

**Date:** 2026-01-11
**Status:** ready
**Priority:** P2 - Important
**Tags:** code-review, error-handling, dotnet, blobs, filesystem

---

## Problem Statement

Different methods use different error handling patterns, making the API confusing and inconsistent:

| Method | Error Handling |
|--------|----------------|
| `UploadAsync` | Logs, swallows, returns void ❌ |
| `BulkUploadAsync` | Returns `Result<Exception>` ✓ |
| `DeleteAsync` | No try-catch, may throw ✓ |
| `BulkDeleteAsync` | Returns `Result<bool, Exception>` ✓ |
| `RenameAsync` | Logs, returns false ⚠️ |
| `CopyAsync` | Logs, returns false ⚠️ |
| `DownloadAsync` | No try-catch, may throw ✓ |

**Why it matters:**
- Developers must read each method signature to understand failure semantics
- Can't establish consistent error handling patterns in calling code
- Some failures are silent, others throw, others return false

---

## Proposed Solutions

### Option A: Standardize on Exception Propagation (Recommended)
Let all methods throw on failure. Remove try-catch from `UploadAsync`, `RenameAsync`, `CopyAsync`.
- **Pros:** Consistent, clear semantics, idiomatic .NET
- **Cons:** Breaking change for callers catching returned bools
- **Effort:** Small
- **Risk:** Medium - behavior change

### Option B: Standardize on Result<T> Pattern
All methods return `Result<T>` or `Result<T, Exception>`:
```csharp
ValueTask<Result<Exception>> UploadAsync(...)
ValueTask<Result<bool, Exception>> DeleteAsync(...)
ValueTask<Result<bool, Exception>> RenameAsync(...)
```
- **Pros:** Explicit error handling, functional style
- **Cons:** Interface breaking change, more verbose
- **Effort:** Large (interface change)
- **Risk:** High - breaking change

### Option C: Document Current Behavior
Add XML documentation explaining each method's error handling.
- **Pros:** No code change
- **Cons:** Doesn't fix inconsistency
- **Effort:** Trivial
- **Risk:** None

---

## Recommended Action

**Option A** - Standardize on exception propagation. This is the most idiomatic .NET pattern.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (multiple methods)

---

## Acceptance Criteria

- [ ] All methods have consistent error handling
- [ ] Error behavior documented
- [ ] Tests verify error propagation

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - pattern-recognition, strict-dotnet-reviewer |
