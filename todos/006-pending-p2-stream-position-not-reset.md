---
status: pending
priority: p2
issue_id: "006"
tags: [code-review, bug, reliability]
dependencies: []
---

# Stream Position Not Reset on Upload Retry

## Problem Statement

In `UploadAsync`, if the initial upload fails with `SftpPathNotFoundException`, the code creates the directory and retries. However, the stream position is NOT reset - the retry will copy zero or fewer bytes.

**Why it matters:** Data loss when auto-creating directories during upload.

## Findings

### From strict-dotnet-reviewer:
- **File:** `src/Framework.Blobs.SshNet/SshBlobStorage.cs:89-116`
```csharp
try
{
    await using var sftpFileStream = await _client.OpenAsync(...);
    await stream.CopyToAsync(sftpFileStream, cancellationToken);  // Stream position at END
}
catch (SftpPathNotFoundException e)
{
    await CreateContainerAsync(container, cancellationToken);

    await using var sftpFileStream = await _client.OpenAsync(...);
    await stream.CopyToAsync(sftpFileStream, cancellationToken);  // Stream still at END!
}
```

## Proposed Solutions

### Option A: Reset stream position if seekable (Recommended)
```csharp
catch (SftpPathNotFoundException e)
{
    await CreateContainerAsync(container, cancellationToken);

    if (stream.CanSeek)
        stream.Position = 0;
    else
        throw new InvalidOperationException(
            "Stream must be seekable for retry. Container did not exist.", e);

    await using var sftpFileStream = await _client.OpenAsync(...);
    await stream.CopyToAsync(sftpFileStream, cancellationToken);
}
```

**Pros:** Handles common case, clear error for edge case
**Cons:** Non-seekable streams will fail
**Effort:** Small
**Risk:** Low

### Option B: Create container first
Check and create container before any stream operations.

**Pros:** Avoids retry logic entirely
**Cons:** Extra network round-trip
**Effort:** Small
**Risk:** Low

## Recommended Action

<!-- Fill after triage -->

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs` (lines 89-116)

## Acceptance Criteria

- [ ] Upload to non-existent container succeeds with correct data
- [ ] Non-seekable streams get clear error message
- [ ] Unit test covers retry scenario

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Identified via code review | Common stream handling pitfall |

## Resources

- Stream.Position: https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.position
