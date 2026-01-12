# LINQ Allocations in Hot Paths

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, performance, blobs-azure, allocations

---

## Problem Statement

Multiple LINQ chains create intermediate allocations in frequently-called paths:

**Pagination (hot path):**
```csharp
// Lines 449, 451
Blobs = blobs.Take(pageSize).ToList(),
ExtraLoadedBlobs = blobs.Skip(pageSize).ToList(),

// Lines 546-547
Blobs = blobs.Take(pageSize).ToList(),
ExtraLoadedBlobs = hasExtraLoadedBlobs ? blobs.Skip(pageSize).ToList() : Array.Empty<BlobInfo>(),
```

**URL normalization:**
```csharp
// Lines 602-604
var prefix = _blobServiceClient.Uri.AbsoluteUri.EnsureEndsWith('/')
    + container.Select(_NormalizeSlashes).JoinAsString('/');
return blobNames.Select(blobName => new Uri($"{prefix}/{blobName}")).ToList();
```

**Blob path normalization (called on EVERY operation):**
```csharp
// Lines 609
var blob = containers.Skip(1).Append(blobName).Select(_NormalizeSlashes).JoinAsString('/');
```

**Why it matters:**
- Each `Take().ToList()` and `Skip().ToList()` creates new List allocation
- For bulk delete with 1000 blobs: ~1000 string allocations + Uri allocations
- Pagination runs per-page, multiplying allocations
- `_NormalizeBlob` called on every single operation (upload, download, delete, etc.)

---

## Proposed Solutions

### Option A: Use List.GetRange Instead of LINQ
```csharp
Blobs = blobs.GetRange(0, Math.Min(pageSize, blobs.Count)),
ExtraLoadedBlobs = blobs.Count > pageSize ? blobs.GetRange(pageSize, blobs.Count - pageSize) : [],
```
- **Pros:** Fewer allocations
- **Cons:** Requires blobs to be List<T>
- **Effort:** Small
- **Risk:** Low

### Option B: Use Span/Memory for Slicing
```csharp
var span = CollectionsMarshal.AsSpan(blobs);
Blobs = span[..pageSize].ToArray(),
```
- **Pros:** Minimal allocations
- **Cons:** More complex, requires List<T>
- **Effort:** Medium
- **Risk:** Low

### Option C: StringBuilder for Path Building
```csharp
private static string _NormalizeBlob(string[] containers, string blobName)
{
    var sb = new StringBuilder();
    for (int i = 1; i < containers.Length; i++)
    {
        if (sb.Length > 0) sb.Append('/');
        sb.Append(_NormalizeSlashes(containers[i]));
    }
    if (sb.Length > 0) sb.Append('/');
    sb.Append(_NormalizeSlashes(blobName));
    return sb.ToString();
}
```
- **Pros:** Single allocation
- **Cons:** More verbose
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

Start with **Option A** for pagination, **Option C** for path building.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/AzureBlobStorage.cs` (lines 449, 451, 546, 547, 598-612)

---

## Acceptance Criteria

- [ ] Pagination doesn't allocate intermediate lists
- [ ] Path normalization uses StringBuilder
- [ ] URL building optimized for bulk operations
- [ ] Benchmarks show improvement

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From performance review |
