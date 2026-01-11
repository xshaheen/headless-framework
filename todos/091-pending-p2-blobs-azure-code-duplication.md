# Code Duplication Between Azure and AWS Providers

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, architecture, blobs-azure, blobs-aws, code-duplication

---

## Problem Statement

Multiple implementations are duplicated between Azure and AWS blob storage providers:

**1. BulkUploadAsync Pattern (Azure lines 111-136, AWS lines 131-155)**
```csharp
var tasks = blobs.Select(async blob =>
{
    try
    {
        await UploadAsync(container, blob.FileName, blob.Stream, blob.Metadata, cancellationToken);
        return Result<Exception>.Ok();
    }
    catch (Exception e) { return Result<Exception>.Fail(e); }
});
return await Task.WhenAll(tasks).WithAggregatedExceptions();
```

**2. SearchCriteria and _GetRequestCriteria (Azure lines 555-581, AWS lines 713-739)**
- Nearly identical record and method

**3. _NormalizePath (Azure lines 624-628, AWS lines 761-765)**
```csharp
private static string? _NormalizePath(string? path)
{
    return path?.Replace('\\', '/');
}
```

**4. Metadata Constants (Azure lines 26-27, AWS lines 27-28)**
```csharp
private const string _UploadDateMetadataKey = "uploadDate";
private const string _ExtensionMetadataKey = "extension";
```

**5. Static Container Cache (Azure line 24, AWS line 24)**
- Same pattern, same issues

**Why it matters:**
- DRY violation
- Bug fixes must be applied twice
- Inconsistent behavior risk
- Maintenance burden

---

## Proposed Solutions

### Option A: Extract to Base Class
```csharp
public abstract class BlobStorageBase : IBlobStorage
{
    protected const string UploadDateMetadataKey = "uploadDate";

    public virtual async ValueTask<IReadOnlyList<Result<Exception>>> BulkUploadAsync(...)
    {
        // Shared implementation
    }

    protected static string? NormalizePath(string? path) => ...;
    protected static SearchCriteria GetRequestCriteria(...) => ...;
}
```
- **Pros:** Standard OOP approach
- **Cons:** Tight coupling, inheritance hierarchy
- **Effort:** Medium
- **Risk:** Medium

### Option B: Extract to Shared Utilities
```csharp
// In Framework.Blobs.Abstractions
public static class BlobStorageUtilities
{
    public const string UploadDateMetadataKey = "uploadDate";
    public static string? NormalizePath(string? path) => ...;
}
```
- **Pros:** Composition over inheritance
- **Cons:** More types to manage
- **Effort:** Small
- **Risk:** Low

### Option C: Extract BulkUploadAsync to Extension Method
```csharp
public static class BlobStorageExtensions
{
    public static async ValueTask<IReadOnlyList<Result<Exception>>> BulkUploadAsync(
        this IBlobStorage storage, ...)
    {
        // Generic implementation using UploadAsync
    }
}
```
- **Pros:** No implementation change needed
- **Cons:** Less control over parallelism per provider
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option B** - Extract shared utilities to `Framework.Blobs.Abstractions`. Constants and pure helper functions can be shared safely.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/AzureBlobStorage.cs`
- `src/Framework.Blobs.Aws/AwsBlobStorage.cs`
- New: `src/Framework.Blobs.Abstractions/BlobStorageUtilities.cs`

---

## Acceptance Criteria

- [ ] Common constants moved to shared location
- [ ] `_NormalizePath` shared
- [ ] `SearchCriteria` shared
- [ ] Both providers use shared code
- [ ] Tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From pattern recognition review |
