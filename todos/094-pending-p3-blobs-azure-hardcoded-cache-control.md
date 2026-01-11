# Hardcoded Cache-Control Header

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, configuration, blobs-azure

---

## Problem Statement

Cache-Control header is hardcoded:

```csharp
private const string _DefaultCacheControl = "max-age=7776000, must-revalidate";
```

90 days TTL is not appropriate for all scenarios (dynamic content, frequently updated files).

**Why it matters:**
- Different use cases need different cache policies
- Static assets vs dynamic content
- No way to customize without code change

---

## Proposed Solutions

### Option A: Add to AzureStorageOptions
```csharp
public class AzureStorageOptions
{
    public string CacheControl { get; set; } = "max-age=7776000, must-revalidate";
}
```
- **Pros:** Configurable
- **Cons:** One value for all blobs
- **Effort:** Small
- **Risk:** Low

### Option B: Add to Upload Method Signature
```csharp
ValueTask UploadAsync(
    ...,
    string? cacheControl = null,
    ...
);
```
- **Pros:** Per-blob control
- **Cons:** API change
- **Effort:** Medium
- **Risk:** Medium

---

## Recommended Action

**Option A** - Add to options. Per-blob control can be added later if needed.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/AzureBlobStorage.cs` (line 25, 97)
- `src/Framework.Blobs.Azure/AzureStorageOptions.cs`

---

## Acceptance Criteria

- [ ] Cache-Control configurable via options
- [ ] Sensible default maintained
- [ ] Documentation updated

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
