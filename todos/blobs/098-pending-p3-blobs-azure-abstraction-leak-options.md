# Azure-Specific Types Leaked in Options

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, architecture, blobs-azure, abstraction-leakage

---

## Problem Statement

`AzureStorageOptions` exposes Azure SDK types:

```csharp
public PublicAccessType ContainerPublicAccessType { get; set; } = PublicAccessType.None;
```

`PublicAccessType` is from `Azure.Storage.Blobs.Models`.

**Why it matters:**
- Consumers configuring blob storage need Azure SDK reference
- Tight coupling to Azure types
- Can't easily port configuration to other providers

---

## Proposed Solutions

### Option A: Create Framework-Level Enum
```csharp
// In Framework.Blobs.Abstractions
public enum ContainerAccessType
{
    None,
    Blob,
    Container
}

// In AzureStorageOptions, convert:
internal PublicAccessType ToAzureAccessType() => ...;
```
- **Pros:** Provider-agnostic configuration
- **Cons:** Mapping layer needed
- **Effort:** Medium
- **Risk:** Low

### Option B: Keep As-Is, Document Dependency
- **Pros:** No change
- **Cons:** Doesn't fix the issue
- **Effort:** None
- **Risk:** Low

---

## Recommended Action

**Option B** for now - this is low priority. Consider **Option A** in future major version.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/AzureStorageOptions.cs` (line 17)

---

## Acceptance Criteria

- [ ] Document Azure SDK dependency in options
- [ ] Consider abstraction for future version

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From architecture review |
