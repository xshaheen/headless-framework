---
status: pending
priority: p1
issue_id: "001"
tags: [code-review, dotnet, thread-safety, critical]
dependencies: []
---

# Thread Safety: Race Condition in SftpClient Connection

## Problem Statement

`SshBlobStorage` uses a singleton `SftpClient` without thread-safe connection management. The `_EnsureClientConnectedAsync` method has a classic check-then-act race condition, and `SftpClient` itself is NOT thread-safe for concurrent operations.

**Why it matters:** Under concurrent load, this WILL cause connection corruption, duplicate connection attempts, and unpredictable failures. Combined with singleton registration, this affects ALL requests in the application.

## Findings

### From strict-dotnet-reviewer:
- **File:** `src/Framework.Blobs.SshNet/SshBlobStorage.cs:842-859`
- Two concurrent callers can both see `IsConnected == false`, then both attempt `ConnectAsync()`
- Even when connected, concurrent operations on `SftpClient` use a single session channel - NOT thread-safe

### From architecture-strategist:
- **File:** `src/Framework.Blobs.SshNet/SshSetup.cs:38`
- Singleton registration means ALL requests share ONE non-thread-safe client

### From performance-oracle:
- Under 10+ concurrent users: Connection contention, potential deadlocks
- Under 100 concurrent users: System WILL fail

## Proposed Solutions

### Option A: SemaphoreSlim for all operations (Recommended)
Add `SemaphoreSlim` to serialize all SFTP operations.

**Pros:**
- Simple implementation
- Maintains singleton pattern
- Thread-safe

**Cons:**
- Reduces concurrency (serializes operations)
- Single connection may become bottleneck

**Effort:** Small
**Risk:** Low

### Option B: Connection pooling with factory pattern
Create `ISftpClientFactory` that manages pool of connections.

**Pros:**
- Better throughput
- True concurrent operations

**Cons:**
- More complex
- Connection pool management overhead

**Effort:** Large
**Risk:** Medium

### Option C: Scoped registration
Change from Singleton to Scoped - new connection per request.

**Pros:**
- Simple change
- No thread-safety concerns

**Cons:**
- Connection overhead per request
- Performance impact

**Effort:** Small
**Risk:** Medium (performance)

## Recommended Action

<!-- Fill after triage -->

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs` (lines 21, 34-39, 842-859)
- `src/Framework.Blobs.SshNet/SshSetup.cs` (line 38)

**Components:** SshBlobStorage, DI registration

## Acceptance Criteria

- [ ] Concurrent upload/download operations don't cause failures
- [ ] Connection establishment is thread-safe
- [ ] Load test with 50+ concurrent operations passes
- [ ] No race conditions under stress testing

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Identified via code review | SftpClient documentation confirms non-thread-safe |

## Resources

- PR: Current implementation review
- SSH.NET docs: https://github.com/sshnet/SSH.NET/wiki
