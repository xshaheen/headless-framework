---
status: pending
priority: p1
issue_id: "135"
tags: [code-review, blobs, aws, performance, security]
dependencies: []
---

# Upload Memory Exhaustion - Full Stream Copy to MemoryStream

## Problem Statement

UploadAsync copies the entire upload stream into a MemoryStream before sending to S3. For large files (e.g., 500MB video uploads), this doubles memory usage and can cause OutOfMemoryException.

## Findings

- **Location:** `src/Framework.Blobs.Aws/AwsBlobStorage.cs:88-90`
```csharp
await using var streamCopy = new MemoryStream();
await stream.CopyToAsync(streamCopy, cancellationToken);
streamCopy.ResetPosition();
```
- Copies ENTIRE stream into memory before upload
- No size validation exists
- Large Object Heap (LOH) allocations cause GC pressure
- DoS vector: malicious upload of 10GB file exhausts server memory

## Proposed Solutions

### Option 1: Check CanSeek and Use Input Stream Directly

```csharp
if (stream.CanSeek)
{
    stream.Position = 0;
    request.InputStream = stream;
    request.AutoCloseStream = false;
}
else
{
    await using var streamCopy = new MemoryStream();
    await stream.CopyToAsync(streamCopy, cancellationToken).AnyContext();
    streamCopy.Position = 0;
    request.InputStream = streamCopy;
}
```

**Effort:** 30 minutes | **Risk:** Low

### Option 2: Add MaxUploadSize Option

Add size limit in options, reject files over threshold.

**Effort:** 1-2 hours | **Risk:** Medium

## Acceptance Criteria

- [ ] Seekable streams used directly without copy
- [ ] MaxUploadSize option added
- [ ] No memory spikes for 100MB+ uploads
