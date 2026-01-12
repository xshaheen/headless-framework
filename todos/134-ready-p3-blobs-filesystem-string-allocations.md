# String Allocations in Hot Path (_BuildBlobPath)

---
status: ready
priority: p3
issue_id: "134"
tags: [performance, allocations, blobs, filesystem]
dependencies: []
---

## Problem Statement

`_BuildBlobPath` is called on every operation and creates intermediate string allocations:

```csharp
// FileSystemBlobStorage.cs:538-546
private string _BuildBlobPath(string[] container, string fileName)
{
    var filePath = Path.Combine(_basePath, Path.Combine(container), fileName);
    //                                     ^^^^^^^^^^^^^^^^^^^^
    //                                     Intermediate string allocation
    return filePath;
}
```

**Impact:** For 10,000 operations/sec, creates 20,000+ string allocations/sec. May cause GC pressure in high-throughput scenarios.

---

## Proposed Solutions

### Option A: Single Path.Combine Call
```csharp
private string _BuildBlobPath(string[] container, string fileName)
{
    var segments = new string[container.Length + 2];
    segments[0] = _basePath;
    container.CopyTo(segments, 1);
    segments[^1] = fileName;
    return Path.Combine(segments);
}
```
- **Pros:** Single allocation for combined path
- **Cons:** Array allocation, more code
- **Effort:** Small
- **Risk:** None

### Option B: Use Span/String.Create ✅ SELECTED
For maximum performance, use `string.Create` with stack-allocated span.
- **Pros:** Minimal allocations
- **Cons:** Complex code
- **Effort:** Medium
- **Risk:** Low

### Option C: Cache Common Paths
```csharp
private readonly ConcurrentDictionary<string, string> _pathCache = new();
```
- **Pros:** Zero allocation for repeated paths
- **Cons:** Memory growth, cache invalidation
- **Effort:** Medium
- **Risk:** Medium

---

## Recommended Action

**Option B** - Use `string.Create` with stack-allocated span for minimal allocations in hot path.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (lines 538-546)

---

## Acceptance Criteria

- [ ] Use `string.Create` with span for path building
- [ ] Reduced allocations
- [ ] No behavior change
- [ ] Tests pass

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - performance-oracle |
| 2026-01-12 | Approved | Triage - selected Option B (Span/String.Create) |
