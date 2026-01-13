---
status: pending
priority: p2
issue_id: "007"
tags: [code-review, performance, scalability]
dependencies: []
---

# O(n^2) Pagination Complexity

## Problem Statement

`_GetFileListAsync` re-enumerates the entire directory structure for each page, then applies skip/take. For large directories, this is extremely expensive.

**Why it matters:** 10,000 files with pageSize=100 requires ~500,500 file reads total instead of ~10,000.

## Findings

### From performance-oracle:
- **File:** `src/Framework.Blobs.SshNet/SshBlobStorage.cs:605-660`
- Comment on line 621-622 acknowledges the issue: "ALERT: This could be expensive..."
- Complexity: Page N requires O(N * pageSize) file reads

**Impact projections:**
- 10,000 files, pageSize=100: ~500,500 file reads
- 100,000 files: ~500 million file operations

## Proposed Solutions

### Option A: Implement IAsyncEnumerable streaming (Recommended)
Enable the commented-out `GetBlobsAsync` in the interface.

**Pros:** O(n) performance, memory efficient
**Cons:** API change (new method)
**Effort:** Medium
**Risk:** Low

### Option B: Cursor-based pagination
Use directory paths as cursors instead of offset/limit.

**Pros:** O(n) performance
**Cons:** More complex implementation
**Effort:** Medium
**Risk:** Medium

### Option C: Document limitation prominently
Add warning to API docs, recommend streaming for large directories.

**Pros:** No code change
**Cons:** Doesn't fix the problem
**Effort:** Trivial
**Risk:** Low

## Recommended Action

<!-- Fill after triage -->

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs` (lines 539-660)
- `src/Framework.Blobs.Abstractions/IBlobStorage.cs` (lines 108-123 - commented out)

## Acceptance Criteria

- [ ] Large directory listing completes in reasonable time
- [ ] Memory usage stays bounded during enumeration
- [ ] Performance test with 10,000+ files passes

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Identified via performance review | Interface has streaming method commented out |

## Resources

- IAsyncEnumerable docs: https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/generate-consume-asynchronous-stream
