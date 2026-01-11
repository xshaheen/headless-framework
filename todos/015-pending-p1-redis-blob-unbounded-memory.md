# Redis Blob Storage Unbounded Memory Allocation

**Date:** 2026-01-11
**Status:** pending
**Priority:** P1 - Critical
**Tags:** code-review, security, performance, dotnet, redis, blobs

---

## Problem Statement

`UploadAsync` copies the entire input stream into a `MemoryStream` without size validation (line 65-66). An attacker could upload an extremely large file causing Out-of-Memory conditions.

```csharp
await using var memory = new MemoryStream();
await stream.CopyToAsync(memory, 0x14000, cancellationToken).AnyContext();
// No size check - memory grows unbounded
```

**Attack Vector:**
- Upload multi-gigabyte file to exhaust server memory
- Denial of Service via memory exhaustion
- Redis itself has limits but server OOMs first

---

## Findings

**From security-sentinel:**
- No maximum file size enforcement
- `DownloadAsync` also loads entire blob into memory (line 374)

**From architecture-strategist:**
- Redis is an in-memory store - large blobs are inappropriate
- No documentation warning about size limits

**From pragmatic-dotnet-reviewer:**
- Redis max value is 512MB but that doesn't mean you *should* store 512MB

---

## Proposed Solutions

### Option A: Configurable Max Blob Size
```csharp
// In RedisBlobStorageOptions
public long MaxBlobSizeBytes { get; set; } = 10 * 1024 * 1024; // 10MB default

// In UploadAsync
if (stream.CanSeek && stream.Length > _options.MaxBlobSizeBytes)
    throw new ArgumentException($"Blob exceeds maximum size of {_options.MaxBlobSizeBytes} bytes");
```
- **Pros:** Configurable, clear error
- **Cons:** Requires seekable stream for pre-check
- **Effort:** Small
- **Risk:** Low

### Option B: Track Bytes During Copy
```csharp
var bytesWritten = 0L;
var buffer = ArrayPool<byte>.Shared.Rent(81920);
try
{
    int read;
    while ((read = await stream.ReadAsync(buffer, ct)) > 0)
    {
        bytesWritten += read;
        if (bytesWritten > _options.MaxBlobSizeBytes)
            throw new InvalidOperationException("Blob size limit exceeded");
        memory.Write(buffer, 0, read);
    }
}
finally { ArrayPool<byte>.Shared.Return(buffer); }
```
- **Pros:** Works with non-seekable streams
- **Cons:** More complex
- **Effort:** Medium
- **Risk:** Low

### Option C: Document and Defer
- Add documentation that Redis blob storage is for small blobs only
- Recommend Azure/S3 for large objects
- **Pros:** Non-breaking
- **Cons:** Doesn't prevent misuse
- **Effort:** Small
- **Risk:** Medium (DoS still possible)

---

## Recommended Action

**Option A + C** - Add configurable limit with sensible default (10MB) AND document the use case.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Redis/RedisBlobStorage.cs` (lines 65-66)
- `src/Framework.Blobs.Redis/RedisBlobStorageOptions.cs`
- `src/Framework.Blobs.Redis/README.md`

---

## Acceptance Criteria

- [ ] Add `MaxBlobSizeBytes` option with 10MB default
- [ ] Validate size before copying to memory
- [ ] Clear error message when limit exceeded
- [ ] Add unit test for size limit enforcement
- [ ] Document in README that Redis is for small/ephemeral blobs

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - security-sentinel, architecture-strategist |
