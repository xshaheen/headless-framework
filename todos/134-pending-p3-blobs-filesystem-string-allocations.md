# String Allocations in Hot Path (_BuildBlobPath)

**Date:** 2026-01-11
**Status:** pending
**Priority:** P3 - Nice-to-Have
**Tags:** code-review, performance, allocations, dotnet, blobs, filesystem

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

### Option B: Use Span/String.Create
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

**Option A** - Single `Path.Combine` call. Simple improvement for moderate gain. Only pursue if profiling shows this is a bottleneck.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.FileSystem/FileSystemBlobStorage.cs` (lines 538-546)

---

## Acceptance Criteria

- [ ] Reduced allocations (if implemented)
- [ ] No behavior change
- [ ] Benchmark shows improvement (if prioritized)

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - performance-oracle |
