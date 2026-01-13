---
status: pending
priority: p2
issue_id: "008"
tags: [code-review, architecture, di]
dependencies: []
---

# Inject ILogger Through DI Instead of LoggerFactory in Options

## Problem Statement

`SshBlobStorageOptions` contains `ILoggerFactory` property, and the constructor manually creates the logger. This is inconsistent with other providers and standard DI patterns.

**Why it matters:** Breaks DI conventions, makes testing harder, inconsistent with codebase.

## Findings

### From pragmatic-dotnet-reviewer:
- **File:** `src/Framework.Blobs.SshNet/SshBlobStorageOptions.cs:21`
```csharp
public ILoggerFactory? LoggerFactory { get; set; }
```

### From architecture-strategist:
- FileSystem and Azure implementations inject `ILogger<T>` directly
- Current pattern requires manual logger creation in constructor

## Proposed Solutions

### Option A: Use primary constructor with ILogger injection (Recommended)
```csharp
public sealed class SshBlobStorage(
    IOptions<SshBlobStorageOptions> optionsAccessor,
    ILogger<SshBlobStorage> logger
) : IBlobStorage
{
    private readonly ILogger _logger = logger;
    // ...
}
```

Remove `LoggerFactory` from options.

**Pros:** Standard DI pattern, consistent with other providers
**Cons:** Breaking change for options
**Effort:** Small
**Risk:** Low

## Recommended Action

<!-- Fill after triage -->

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs` (constructor)
- `src/Framework.Blobs.SshNet/SshBlobStorageOptions.cs` (remove LoggerFactory)

## Acceptance Criteria

- [ ] Logger injected via constructor
- [ ] LoggerFactory removed from options
- [ ] Tests still work
- [ ] Consistent with FileSystem/Azure implementations

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Identified via architecture review | Standard .NET DI pattern |

## Resources

- ILogger DI: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging
