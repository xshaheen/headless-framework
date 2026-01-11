# Logger Passed via Options Instead of DI

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, architecture, blobs-azure, dependency-injection

---

## Problem Statement

`AzureStorageOptions` contains `ILoggerFactory`:

```csharp
public ILoggerFactory? LoggerFactory { get; set; }
```

And `AzureBlobStorage` creates logger from options:

```csharp
_logger = _option.LoggerFactory?.CreateLogger<AzureBlobStorage>() ?? NullLogger<AzureBlobStorage>.Instance;
```

**Why it matters:**
- Bypasses standard .NET DI pattern
- Makes testing harder (must configure options instead of mocking ILogger)
- Inconsistent with rest of framework
- Logger should be injected directly

---

## Proposed Solutions

### Option A: Inject ILogger Directly
```csharp
public AzureBlobStorage(
    BlobServiceClient blobServiceClient,
    IMimeTypeProvider mimeTypeProvider,
    IClock clock,
    IOptions<AzureStorageOptions> optionAccessor,
    ILogger<AzureBlobStorage> logger  // Add this
)
{
    _logger = logger;
}
```
Remove `LoggerFactory` from `AzureStorageOptions`.
- **Pros:** Standard pattern, testable
- **Cons:** Breaking change for existing consumers
- **Effort:** Small
- **Risk:** Medium (breaking)

### Option B: Keep Both, Prefer Injected
```csharp
public AzureBlobStorage(
    ...,
    ILogger<AzureBlobStorage>? logger = null
)
{
    _logger = logger
        ?? _option.LoggerFactory?.CreateLogger<AzureBlobStorage>()
        ?? NullLogger<AzureBlobStorage>.Instance;
}
```
- **Pros:** Backward compatible
- **Cons:** More complex, two ways to configure
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Inject `ILogger<AzureBlobStorage>` directly. Document as breaking change.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/AzureBlobStorage.cs` (constructor)
- `src/Framework.Blobs.Azure/AzureStorageOptions.cs` (remove LoggerFactory)
- Same pattern in AWS and FileSystem implementations

---

## Acceptance Criteria

- [ ] `ILogger<AzureBlobStorage>` injected via constructor
- [ ] `LoggerFactory` removed from options
- [ ] Tests updated
- [ ] Breaking change documented

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From architecture review |
