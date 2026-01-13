---
status: pending
priority: p2
issue_id: "010"
tags: [code-review, dotnet, async]
dependencies: []
---

# Implement IAsyncDisposable

## Problem Statement

`SshBlobStorage` is an async-first class but only implements synchronous `IDisposable`. The `SftpClient` supports `DisconnectAsync()`.

**Why it matters:** Synchronous disposal in async code blocks calling thread; modern pattern expects `IAsyncDisposable`.

## Findings

### From pragmatic-dotnet-reviewer:
- **File:** `src/Framework.Blobs.SshNet/SshBlobStorage.cs:958-978`
- All operations are async (`ValueTask`), but disposal is sync
- `SftpClient` supports async disconnect

## Proposed Solutions

### Option A: Add IAsyncDisposable (Recommended)
```csharp
public async ValueTask DisposeAsync()
{
    if (_client.IsConnected)
    {
        await _client.DisconnectAsync(CancellationToken.None).AnyContext();
    }
    _client.Dispose();
}

public void Dispose()
{
    DisposeAsync().AsTask().GetAwaiter().GetResult();
}
```

**Pros:** Modern async pattern, doesn't block
**Cons:** Adds method
**Effort:** Small
**Risk:** Very Low

## Recommended Action

<!-- Fill after triage -->

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs` (lines 958-978)

## Acceptance Criteria

- [ ] Implements IAsyncDisposable
- [ ] await using works correctly
- [ ] Sync Dispose still works for backwards compatibility

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Identified via code review | Modern C# best practice |

## Resources

- IAsyncDisposable: https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-disposeasync
