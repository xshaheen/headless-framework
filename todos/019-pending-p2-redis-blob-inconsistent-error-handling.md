# Redis Blob Storage Inconsistent Error Handling

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, api-design, dotnet, redis, blobs

---

## Problem Statement

Methods have mixed error handling strategies - some throw, others return false/null:

| Method | Behavior | Strategy |
|--------|----------|----------|
| `UploadAsync` | Catches, logs, **rethrows** | Throw |
| `RenameAsync` | Catches, logs, **returns false** | Swallow |
| `CopyAsync` | Catches, logs, **returns false** | Swallow |
| `DeleteAsync` | No try-catch, **propagates** | Throw |
| `DownloadAsync` | Returns **null** for not found | Return null |

**Problems:**
- Callers cannot predict whether to use try-catch or check return values
- Swallowing exceptions hides actual failures
- `false` can mean "not found" OR "operation failed" - ambiguous

---

## Findings

**From pattern-recognition-specialist:**
- Anti-pattern: Inconsistent exception handling violates Principle of Least Astonishment

**From strict-dotnet-reviewer:**
- Caller cannot distinguish "blob didn't exist" from "Redis connection failed"

---

## Proposed Solutions

### Option A: All Methods Throw on Failure
```csharp
// RenameAsync
catch (Exception e)
{
    _logger.LogError(e, "Error renaming...");
    throw;  // Let caller handle
}
```
- **Pros:** Consistent, explicit
- **Cons:** Breaking change for consumers expecting false
- **Effort:** Small
- **Risk:** Medium (breaking)

### Option B: Return Result<T> Types Consistently
```csharp
public ValueTask<Result<bool, BlobError>> RenameAsync(...);
// BlobError has: NotFound, OperationFailed, etc.
```
- **Pros:** Explicit, no exceptions for expected cases
- **Cons:** API change, more complex
- **Effort:** Medium
- **Risk:** Medium (breaking)

### Option C: Document Current Behavior
- Add XML docs explaining each method's error semantics
- **Pros:** Non-breaking
- **Cons:** Inconsistency remains
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** for new major version, **Option C** for immediate improvement.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Redis/RedisBlobStorage.cs` (lines 257-274, 307-314)

**Affected Methods:**
- `RenameAsync`
- `CopyAsync`

---

## Acceptance Criteria

- [ ] Decide on error handling strategy (throw vs return)
- [ ] Apply consistently across all methods
- [ ] Add XML documentation for error behavior
- [ ] Update README with error handling guidance

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - pattern-recognition-specialist |
