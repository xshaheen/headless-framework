---
status: pending
priority: p2
issue_id: "140"
tags: [code-review, blobs, aws, resource-leak]
dependencies: []
---

# DownloadAsync Resource Leak - Response Not Disposed on NotFound

## Problem Statement

When HttpStatusCode is NotFound, response not disposed before returning null.

## Findings

- **Location:** `src/Framework.Blobs.Aws/AwsBlobStorage.cs:533-536`
```csharp
if (response.HttpStatusCode is HttpStatusCode.NotFound)
{
    return null;  // LEAK: response not disposed!
}
```

## Proposed Solutions

### Option 1: Dispose on NotFound

```csharp
if (response.HttpStatusCode is HttpStatusCode.NotFound)
{
    response.Dispose();
    return null;
}
```

**Effort:** 5 minutes | **Risk:** Low

## Acceptance Criteria

- [ ] Response disposed on NotFound status
