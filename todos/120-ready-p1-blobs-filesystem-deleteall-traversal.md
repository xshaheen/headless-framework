# Directory Traversal in DeleteAllAsync via blobSearchPattern

**Date:** 2026-01-11
**Status:** ready
**Priority:** P1 - Critical
**Tags:** code-review, security, dotnet, blobs, filesystem

---

## Problem Statement

The `blobSearchPattern` parameter in `DeleteAllAsync` is directly combined with the directory path without validation. Attackers can delete arbitrary directories outside the base path.

```csharp
// FileSystemBlobStorage.cs:185-186
blobSearchPattern = blobSearchPattern.NormalizePath();
var path = Path.Combine(directoryPath, blobSearchPattern);  // blobSearchPattern = "../../../important"

// Lines 203-204
var count = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories).Count();
Directory.Delete(directory, recursive: true);  // Deletes arbitrary directory!
```

**Attack Vector:**
```csharp
await blobStorage.DeleteAllAsync(["container"], "../../../var/log/");
// Deletes /var/log/ recursively!
```

**Impact:** Complete directory tree deletion outside the storage boundary. This is a separate vulnerability from the general path traversal issue because the search pattern has different handling.

---

## Proposed Solutions

### Option A: Validate Final Path (Recommended)
```csharp
public ValueTask<int> DeleteAllAsync(
    string[] container,
    string? blobSearchPattern = null,
    CancellationToken cancellationToken = default
)
{
    cancellationToken.ThrowIfCancellationRequested();

    var directoryPath = _GetDirectoryPath(container);

    if (!string.IsNullOrEmpty(blobSearchPattern))
    {
        blobSearchPattern = blobSearchPattern.NormalizePath();
        var fullPath = Path.GetFullPath(Path.Combine(directoryPath, blobSearchPattern));

        if (!fullPath.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Path traversal detected", nameof(blobSearchPattern));
        }
    }
    // ...
}
```
- **Pros:** Complete protection, clear error
- **Cons:** None
- **Effort:** Small
- **Risk:** None

### Option B: Reject Patterns with `..`
```csharp
if (blobSearchPattern?.Contains("..") == true)
{
    throw new ArgumentException("Path traversal patterns not allowed", nameof(blobSearchPattern));
}
```
- **Pros:** Simple, fast
- **Cons:** May miss edge cases with encoded characters
- **Effort:** Trivial
- **Risk:** Medium - incomplete

---

## Recommended Action

**Option A** - Validate final path. This is the only complete solution.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (lines 157-238)

---

## Acceptance Criteria

- [ ] Path traversal via `..` in blobSearchPattern throws
- [ ] Tests verify attack prevention
- [ ] Valid search patterns still work

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - security-sentinel |
