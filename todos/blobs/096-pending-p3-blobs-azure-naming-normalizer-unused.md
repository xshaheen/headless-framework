# IBlobNamingNormalizer Registered but Unused

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, architecture, blobs-azure, dead-code

---

## Problem Statement

`Setup.cs` registers `IBlobNamingNormalizer`:

```csharp
services.TryAddSingleton<IBlobNamingNormalizer, AzureBlobNamingNormalizer>();
```

But `AzureBlobStorage` never injects or uses it. The normalizer exists but is dead code.

**Why it matters:**
- Confusing - appears to provide normalization but doesn't
- Dead DI registration
- Related to path traversal issue (normalizer should be used for security)

---

## Proposed Solutions

### Option A: Inject and Use Normalizer
```csharp
public AzureBlobStorage(
    ...,
    IBlobNamingNormalizer normalizer
)
{
    _normalizer = normalizer;
}

// In methods:
blobName = _normalizer.NormalizeBlobName(blobName);
containerName = _normalizer.NormalizeContainerName(containerName);
```
- **Pros:** Uses existing infrastructure
- **Cons:** Need to implement actual validation in normalizer
- **Effort:** Medium
- **Risk:** Low

### Option B: Remove Registration
```csharp
// Remove from Setup.cs
services.TryAddSingleton<IBlobNamingNormalizer, AzureBlobNamingNormalizer>();
```
- **Pros:** Removes dead code
- **Cons:** Loses potential security layer
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Inject and use normalizer. This is linked to path traversal fix (todo #083).

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/Setup.cs` (line 37)
- `src/Framework.Blobs.Azure/AzureBlobStorage.cs`

---

## Acceptance Criteria

- [ ] Normalizer injected into AzureBlobStorage
- [ ] Normalizer used for blob/container names
- [ ] Or: registration removed if not needed

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review |
