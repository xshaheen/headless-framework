# Redis Blob Storage Error-Level Logging for Normal Conditions

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-have
**Tags:** code-review, logging, dotnet, redis, blobs

---

## Problem Statement

"File not found" logged at ERROR level (lines 369, 400):

```csharp
_logger.LogError("Unable to get file stream for {Path}: File Not Found", blobPath);
_logger.LogError("Unable to get file info for {Path}: File Not Found", blobPath);
```

**Issue:** "Not found" is a normal condition, not an error. This pollutes error logs and may trigger alerts.

---

## Proposed Solutions

### Option A: Change to LogWarning or LogDebug
```csharp
_logger.LogDebug("File not found: {Path}", blobPath);
```
- **Effort:** Small
- **Risk:** Low

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Redis/RedisBlobStorage.cs` (lines 369, 400)

---

## Acceptance Criteria

- [ ] Change LogError to LogDebug for "not found" conditions
- [ ] Reserve LogError for actual errors (connection failures, etc.)

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - strict-dotnet-reviewer |
