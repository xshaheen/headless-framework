# Logger Passed via Options Instead of DI

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, architecture, dotnet, blobs, filesystem

---

## Problem Statement

The logger is passed through options rather than standard DI injection. This is inconsistent with ASP.NET Core conventions and makes testing harder.

```csharp
// FileSystemBlobStorageOptions.cs:12
public ILoggerFactory? LoggerFactory { get; set; }

// FileSystemBlobStorage.cs:26
_logger = options.LoggerFactory?.CreateLogger(typeof(FileSystemBlobStorage)) ?? NullLogger.Instance;
```

**Why it matters:**
- Non-standard pattern - .NET developers expect `ILogger<T>` injection
- Makes testing harder (need to configure options with logger factory)
- Inconsistent with ASP.NET Core conventions
- Options should contain configuration, not services

---

## Proposed Solutions

### Option A: Inject ILogger<T> Directly (Recommended)
```csharp
public sealed class FileSystemBlobStorage(
    IOptions<FileSystemBlobStorageOptions> optionsAccessor,
    ILogger<FileSystemBlobStorage> logger
) : IBlobStorage
{
    private readonly ILogger _logger = logger;
    // ...
}
```
- **Pros:** Standard pattern, easier testing, follows conventions
- **Cons:** Breaking change to constructor
- **Effort:** Small
- **Risk:** Low - internal constructor change

### Option B: Make Logger Optional with Fallback
```csharp
public sealed class FileSystemBlobStorage(
    IOptions<FileSystemBlobStorageOptions> optionsAccessor,
    ILogger<FileSystemBlobStorage>? logger = null
) : IBlobStorage
{
    private readonly ILogger _logger = logger ?? NullLogger<FileSystemBlobStorage>.Instance;
    // ...
}
```
- **Pros:** Backwards compatible
- **Cons:** Nullable parameter is unusual
- **Effort:** Small
- **Risk:** None

---

## Recommended Action

**Option A** - Use standard `ILogger<T>` injection. Remove `LoggerFactory` from options.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (constructor)
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorageOptions.cs` (remove LoggerFactory property)

---

## Acceptance Criteria

- [ ] Logger injected via constructor
- [ ] LoggerFactory removed from options
- [ ] Tests updated to inject mock logger

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - architecture-strategist, pragmatic-dotnet-reviewer |
