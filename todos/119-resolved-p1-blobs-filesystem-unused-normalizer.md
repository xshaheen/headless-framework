# IBlobNamingNormalizer Registered But Never Used

**Date:** 2026-01-11
**Status:** resolved
**Priority:** P1 - Critical
**Tags:** code-review, security, dotnet, blobs, filesystem

---

## Problem Statement

The `CrossOsNamingNormalizer` is registered in DI but **never injected or used** by `FileSystemBlobStorage`. This creates a false sense of security - the normalizer exists but isn't protecting anything.

```csharp
// Setup.cs:39-40
services.TryAddSingleton<IBlobNamingNormalizer, CrossOsNamingNormalizer>();
services.AddSingleton<IBlobStorage, FileSystemBlobStorage>();

// FileSystemBlobStorage constructor - does NOT inject normalizer!
public FileSystemBlobStorage(IOptions<FileSystemBlobStorageOptions> optionsAccessor)
{
    var options = optionsAccessor.Value;
    _basePath = options.BaseDirectoryPath.NormalizePath()...
    // No IBlobNamingNormalizer!
}
```

**Why it matters:**
- Security control exists but is not applied
- Consumers may expect blob names to be normalized
- Inconsistent with intention of registering the normalizer
- Related to path traversal vulnerability - normalizer could help filter dangerous characters

---

## Proposed Solutions

### Option A: Inject and Use Normalizer (Recommended)
```csharp
public sealed class FileSystemBlobStorage(
    IOptions<FileSystemBlobStorageOptions> optionsAccessor,
    IBlobNamingNormalizer normalizer
) : IBlobStorage
{
    private readonly IBlobNamingNormalizer _normalizer = normalizer;

    private string _BuildBlobPath(string[] container, string fileName)
    {
        var normalizedFileName = _normalizer.NormalizeBlobName(fileName);
        var normalizedContainer = container.Select(_normalizer.NormalizeBlobName).ToArray();
        // ...
    }
}
```
- **Pros:** Uses existing infrastructure, consistent behavior
- **Cons:** May change behavior if normalizer strips characters
- **Effort:** Small
- **Risk:** Medium - potential breaking change

### Option B: Remove Normalizer Registration
```csharp
// Setup.cs - remove this line
// services.TryAddSingleton<IBlobNamingNormalizer, CrossOsNamingNormalizer>();
```
- **Pros:** No dead code
- **Cons:** Loses potential security benefit
- **Effort:** Trivial
- **Risk:** Low

### Option C: Make Normalizer Optional via Options
```csharp
public class FileSystemBlobStorageOptions
{
    public bool UseNamingNormalizer { get; set; } = true;
}
```
- **Pros:** Configurable, backwards compatible
- **Cons:** More complexity
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option A** - Inject and use the normalizer. It's registered for a reason. Combine with path traversal fix for defense in depth.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (constructor)
- `src/Framework.Blobs.FileSystem/Setup.cs` (line 39)

**Note:** Should verify what `CrossOsNamingNormalizer` actually filters. If it filters `..` and path separators, it could partially mitigate path traversal.

---

## Acceptance Criteria

- [x] Normalizer injected into FileSystemBlobStorage
- [x] Blob names normalized before path construction
- [x] Container segments normalized
- [ ] Tests verify normalization applied

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - architecture-strategist, security-sentinel |
| 2026-01-12 | Approved for Work | Triaged - Status: pending → ready. Inject normalizer and use it to normalize blob names before path construction for defense in depth. |
| 2026-01-12 | Implemented | Injected IBlobNamingNormalizer via primary constructor, normalized blob names and container segments in _BuildBlobPath and _GetDirectoryPath methods. Also added GetBlobsAsync implementation to satisfy IBlobStorage interface. |
