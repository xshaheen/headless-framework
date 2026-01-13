---
status: pending
priority: p1
issue_id: "003"
tags: [code-review, dotnet, async, critical]
dependencies: []
---

# Missing AnyContext() on 40+ Async Calls

## Problem Statement

The codebase convention requires `AnyContext()` (equivalent to `ConfigureAwait(false)`) on all async operations in library code. Only ONE usage exists at line 556; all other ~40+ await calls are missing it.

**Why it matters:** Without `AnyContext()`, async continuations capture synchronization context. While ASP.NET Core has no SynchronizationContext by default, other hosting environments can experience deadlocks and performance degradation.

## Findings

### From strict-dotnet-reviewer:
- **File:** `src/Framework.Blobs.SshNet/SshBlobStorage.cs`
- Only line 556 uses `AnyContext()`: `await result.NextPageAsync(cancellationToken).AnyContext();`
- ~40+ other await calls missing, including:
  - All `_client.ExistsAsync()`, `_client.CreateDirectoryAsync()`, `_client.OpenAsync()` calls
  - All `stream.CopyToAsync()` calls
  - All `Task.WhenAll()` calls
  - All internal method calls like `_EnsureClientConnectedAsync()`

### From pattern-recognition-specialist:
- AWS and FileSystem implementations use `AnyContext()` consistently
- Inconsistency with codebase patterns

## Proposed Solutions

### Option A: Add AnyContext() to all async calls (Recommended)
Systematic addition of `.AnyContext()` to every await.

**Pros:** Consistent with codebase conventions, prevents deadlocks
**Cons:** Verbose
**Effort:** Small (mechanical change)
**Risk:** Very Low

### Option B: Use global ConfigureAwait
Set assembly-level attribute if available.

**Pros:** One-time change
**Cons:** May not be supported, less explicit
**Effort:** Small
**Risk:** Low

## Recommended Action

<!-- Fill after triage -->

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs` (nearly every async method)

**Lines requiring changes (non-exhaustive):**
- 36, 58, 64, 91-96, 98, 103, 107-112, 114, 135, 145, 161, 167, 174, 207, 215, 224, 231, 238, 241-246, 253, 282, 291, 296, 303, 333, 341, 344, 350, 363, 365, 401, 413, 422, 454, 460, 478, 486, 507, 514, 578-585, 617, 633-640, 684, 727-734, 851

## Acceptance Criteria

- [ ] All await statements use `.AnyContext()`
- [ ] No raw awaits remain (except for test code)
- [ ] Code compiles and tests pass

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Identified via code review | CLAUDE.md requires AnyContext() for library code |

## Resources

- ConfigureAwait FAQ: https://devblogs.microsoft.com/dotnet/configureawait-faq/
