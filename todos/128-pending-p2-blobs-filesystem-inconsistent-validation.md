# Inconsistent Argument Validation Across Methods

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, validation, dotnet, blobs, filesystem

---

## Problem Statement

Different methods use different validation patterns for similar parameters:

| Method | blobName/fileName validation | container validation |
|--------|------------------------------|---------------------|
| `UploadAsync` | `IsNotNullOrEmpty` | `IsNotNullOrEmpty` |
| `DeleteAsync` | None | None |
| `ExistsAsync` | None | None |
| `RenameAsync` | `IsNotNullOrWhiteSpace` | `IsNotNullOrEmpty` |
| `CopyAsync` | `IsNotNullOrWhiteSpace` | `IsNotNullOrEmpty` |
| `GetBlobInfoAsync` | `IsNotNull` | `IsNotNull` |
| `_BuildBlobPath` | `IsNotNullOrWhiteSpace` | `IsNotNullOrEmpty` |

**Why it matters:**
- Inconsistent behavior for invalid input
- Some methods may throw NullReferenceException instead of ArgumentException
- Private method validation is redundant if public methods already validate

---

## Proposed Solutions

### Option A: Standardize All Public Methods (Recommended)
Use `Argument.IsNotNullOrWhiteSpace` for all string parameters and `Argument.IsNotNullOrEmpty` for all array parameters:
```csharp
public ValueTask<bool> DeleteAsync(
    string[] container,
    string blobName,
    CancellationToken cancellationToken = default
)
{
    cancellationToken.ThrowIfCancellationRequested();
    Argument.IsNotNullOrEmpty(container);
    Argument.IsNotNullOrWhiteSpace(blobName);  // Add this

    var filePath = _BuildBlobPath(container, blobName);
    // ...
}
```
- **Pros:** Consistent, clear error messages
- **Cons:** Minor code additions
- **Effort:** Small
- **Risk:** None

### Option B: Remove Redundant Private Method Validation
If all public methods validate, private methods don't need to:
```csharp
private string _BuildBlobPath(string[] container, string fileName)
{
    // Remove validation - callers already validated
    var filePath = Path.Combine(_basePath, Path.Combine(container), fileName);
    return filePath;
}
```
- **Pros:** Less code, no double validation
- **Cons:** Relies on caller discipline
- **Effort:** Trivial
- **Risk:** Low

---

## Recommended Action

**Option A** - Standardize public methods, then **Option B** - remove private validation.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (multiple methods)

---

## Acceptance Criteria

- [ ] All public methods validate all parameters
- [ ] Consistent validation pattern used
- [ ] Clear ArgumentException messages

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - pattern-recognition, strict-dotnet-reviewer |
