# Path Traversal Vulnerability in Blob Names

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, security, blobs-azure, path-traversal, input-validation

---

## Problem Statement

The `_NormalizeBlob` and `_GetBlobClient` methods do not sanitize blob names for path traversal sequences. While backslashes are converted to forward slashes, `../` sequences are NOT stripped.

```csharp
private static string? _NormalizePath(string? path)
{
    return path?.Replace('\\', '/');  // Only converts \ to /
}
```

**Attack Vector:**
```csharp
await blobStorage.UploadAsync(
    ["container"],
    "../other-container/malicious.txt",  // No validation!
    stream
);
```

**Why it matters:**
- Attacker could traverse directories within storage account
- May access blobs in unintended containers
- Azure SDK may reject some malformed paths, but defense should be at application layer

**Related Issue:** `IBlobNamingNormalizer` is registered but NEVER injected or used in `AzureBlobStorage`. The `NormalizeBlobName()` method is a pass-through:
```csharp
public string NormalizeBlobName(string blobName)
{
    return blobName;  // No sanitization!
}
```

---

## Proposed Solutions

### Option A: Validate and Reject Path Traversal
```csharp
if (blobName.Contains("../") || blobName.Contains("..\\"))
    throw new ArgumentException("Path traversal not allowed", nameof(blobName));
```
- **Pros:** Simple, explicit rejection
- **Cons:** Throws on potentially legitimate use
- **Effort:** Small
- **Risk:** Low

### Option B: Strip Path Traversal Sequences
```csharp
blobName = blobName.Replace("../", "").Replace("..\\", "");
```
- **Pros:** Allows operation to proceed
- **Cons:** May silently change intended behavior
- **Effort:** Small
- **Risk:** Medium

### Option C: Inject and Use IBlobNamingNormalizer
```csharp
public AzureBlobStorage(..., IBlobNamingNormalizer normalizer)
{
    _normalizer = normalizer;
}
// In methods:
blobName = _normalizer.NormalizeBlobName(blobName);
```
And implement proper validation in `AzureBlobNamingNormalizer.NormalizeBlobName()`.
- **Pros:** Uses existing infrastructure, consistent pattern
- **Cons:** Requires implementing actual validation
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option C** - Inject `IBlobNamingNormalizer` and implement proper validation. This uses the existing infrastructure that's currently dead code.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/AzureBlobStorage.cs` (lines 589-596, 607-628)
- `src/Framework.Blobs.Azure/AzureBlobNamingNormalizer.cs` (lines 53-56)

---

## Acceptance Criteria

- [ ] `IBlobNamingNormalizer` injected into `AzureBlobStorage`
- [ ] `NormalizeBlobName` validates/strips `../` sequences
- [ ] Path traversal attempts are rejected or sanitized
- [ ] Unit tests cover path traversal scenarios

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From security review |
