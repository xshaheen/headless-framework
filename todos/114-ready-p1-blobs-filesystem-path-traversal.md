# Path Traversal Vulnerability in FileSystem Blob Storage

**Date:** 2026-01-11
**Status:** ready
**Priority:** P1 - Critical
**Tags:** code-review, security, dotnet, blobs, filesystem

---

## Problem Statement

The `_BuildBlobPath` and `_GetDirectoryPath` methods do not validate that the resulting path stays within the base directory. An attacker could supply container or blob names like `../../../etc/passwd` to escape the sandbox.

```csharp
// FileSystemBlobStorage.cs:538-546
private string _BuildBlobPath(string[] container, string fileName)
{
    Argument.IsNotNullOrWhiteSpace(fileName);
    Argument.IsNotNullOrEmpty(container);

    var filePath = Path.Combine(_basePath, Path.Combine(container), fileName);
    return filePath;  // No validation that path stays within _basePath!
}
```

**Attack Vectors:**
1. Container array: `["uploads", "..", "..", "etc"]` with blobName `"passwd"` -> reads `/etc/passwd`
2. BlobName: `"../../../etc/shadow"` -> reads `/etc/shadow`
3. Mixed: `["blobs"]` with blobName `"../../.ssh/authorized_keys"` -> write to SSH keys

**Impact:**
- **Confidentiality:** Arbitrary file read (download, exists, list)
- **Integrity:** Arbitrary file write/overwrite (upload, copy, rename)
- **Availability:** Arbitrary file deletion (delete, deleteAll)

**CVSS Estimate:** 9.8 (Critical)

---

## Proposed Solutions

### Option A: Add Path Canonicalization with Containment Check (Recommended)
```csharp
private string _ValidateAndBuildPath(params string[] segments)
{
    var combined = Path.GetFullPath(Path.Combine(_basePath, Path.Combine(segments)));

    if (!combined.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
    {
        throw new ArgumentException("Path traversal detected");
    }

    return combined;
}
```
- **Pros:** Comprehensive protection, clear error message
- **Cons:** Slight performance overhead from path normalization
- **Effort:** Small
- **Risk:** Low

### Option B: Validate Path Segments for Dangerous Characters
```csharp
private static void _ValidatePathSegments(string[] segments)
{
    foreach (var segment in segments)
    {
        if (string.IsNullOrWhiteSpace(segment))
            throw new ArgumentException("Path segment cannot be empty");
        if (segment.Contains("..") || Path.IsPathRooted(segment))
            throw new ArgumentException("Path traversal not allowed");
    }
}
```
- **Pros:** Fails fast on obvious attacks
- **Cons:** May miss edge cases, path normalization still needed
- **Effort:** Trivial
- **Risk:** Medium - incomplete protection

### Option C: Use IBlobNamingNormalizer (Already Registered)
The `CrossOsNamingNormalizer` is already registered in DI but never injected/used by `FileSystemBlobStorage`. Inject and use it to filter dangerous characters.
- **Pros:** Uses existing infrastructure
- **Cons:** Need to verify normalizer catches all dangerous patterns
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Add path canonicalization. This is the only complete solution. Combine with Option B for defense in depth.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (lines 538-555)

**All affected methods:**
- `UploadAsync`, `BulkUploadAsync`, `DeleteAsync`, `BulkDeleteAsync`, `DeleteAllAsync`
- `RenameAsync`, `CopyAsync`, `ExistsAsync`, `DownloadAsync`, `GetBlobInfoAsync`
- `GetPagedListAsync`

---

## Acceptance Criteria

- [ ] Path traversal via `..` in container segments throws ArgumentException
- [ ] Path traversal via `..` in blobName throws ArgumentException
- [ ] Absolute paths in segments are rejected
- [ ] Final path always starts with base directory
- [ ] Unit tests for all attack vectors
- [ ] Security documentation updated

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - security-sentinel, strict-dotnet-reviewer |
