# Redis Blob Storage Inconsistent Argument Validation

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-have
**Tags:** code-review, correctness, dotnet, redis, blobs

---

## Problem Statement

Inconsistent use of `IsNotNull` vs `IsNotNullOrEmpty`:

| Method | Parameter Check | Line |
|--------|-----------------|------|
| `UploadAsync` | `IsNotNullOrEmpty` | 55-56 |
| `DeleteAsync` | `IsNotNull` | 139-140 |
| `RenameAsync` | `IsNotNull` | 248-251 |
| `ExistsAsync` | `IsNotNull` | 327-328 |
| `DownloadAsync` | `IsNotNull` | 352-353 |
| `GetBlobInfoAsync` | Mixed | 385-386 |

**Issue:** `IsNotNull` allows empty strings which may cause unexpected Redis key behavior.

---

## Proposed Solutions

### Option A: Standardize to IsNotNullOrEmpty
- Apply `IsNotNullOrEmpty` consistently for blobName and container
- **Effort:** Small
- **Risk:** Low (may catch previously silent bugs)

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Redis/RedisBlobStorage.cs` (lines 139-140, 248-251, 288-291, 327-328, 352-353)

---

## Acceptance Criteria

- [ ] All blobName checks use IsNotNullOrEmpty
- [ ] All container checks use IsNotNullOrEmpty

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - pattern-recognition-specialist |
