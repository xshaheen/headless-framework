# Missing AnyContext() on Async Calls

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, dotnet, blobs-azure, async, convention-violation

---

## Problem Statement

Nearly all `await` calls in `AzureBlobStorage.cs` lack `.AnyContext()` extension (project convention for `ConfigureAwait(false)`). Only lines 211 and 408 use it correctly.

**Why it matters:**
- Violates project convention explicitly stated in CLAUDE.md
- Causes unnecessary context capture in library code
- Potential deadlocks if called from sync-over-async context
- Inconsistent with other providers (AWS uses AnyContext consistently)

---

## Findings

**Affected lines in `AzureBlobStorage.cs`:**
- Line 64: `await containerClient.CreateIfNotExistsAsync(...)`
- Line 104: `await blobClient.UploadAsync(...)`
- Line 134: `await Task.WhenAll(tasks).WithAggregatedExceptions()`
- Line 149-152: `await batch.DeleteBlobsAsync(...)`
- Line 180-184: `await batch.DeleteBlobsAsync(...)`
- Line 203: `await GetPagedListAsync(...)`
- Line 209: `await BulkDeleteAsync(...)`
- Line 251: `await newBlobClient.StartCopyFromUriAsync(...)`
- Line 256: `await copyResult.WaitForCompletionAsync(...)`
- Line 283: `await CopyAsync(...)`
- Line 292: `await DeleteAsync(...)`
- Line 315: `await blobClient.ExistsAsync(...)`
- Line 336: `await blobClient.DownloadToAsync(...)`
- Line 364: `await blobClient.GetPropertiesAsync(...)`
- Line 405: `await _GetFilesAsync(...)`

Also in `AzureNextPageResult.cs` line 24.

---

## Proposed Solutions

### Option A: Add AnyContext() to All Await Calls
- **Pros:** Follows convention, consistent with other providers
- **Cons:** Tedious but straightforward
- **Effort:** Small (mechanical change)
- **Risk:** Low

---

## Recommended Action

**Option A** - Add `.AnyContext()` to all await calls.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/AzureBlobStorage.cs` (~25 locations)
- `src/Framework.Blobs.Azure/Internals/AzureNextPageResult.cs` (1 location)

---

## Acceptance Criteria

- [ ] All await calls in AzureBlobStorage.cs use .AnyContext()
- [ ] All await calls in AzureNextPageResult.cs use .AnyContext()
- [ ] Tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
