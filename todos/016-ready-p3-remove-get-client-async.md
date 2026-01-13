---
status: ready
priority: p3
issue_id: "016"
tags: [code-review, api-design, encapsulation]
dependencies: []
---

# Remove GetClientAsync Public Method

## Problem Statement

`GetClientAsync()` exposes the internal `SftpClient` directly. This breaks encapsulation and is not part of the `IBlobStorage` interface.

**Why it matters:** Leaks implementation details, breaks abstraction.

## Findings

### From architecture-strategist:
- **File:** `src/Framework.Blobs.SshNet/SshBlobStorage.cs:34-41`
- Not in interface, exposes internal client
- Tests use it: `storage is SshBlobStorage sshStorage ? await sshStorage.GetClientAsync(...)`

### From simplicity-reviewer:
- YAGNI: No evidence of external usage

## Proposed Solutions

### Option A: Remove method (Recommended)
Delete `GetClientAsync` entirely.

**Pros:** Better encapsulation
**Cons:** Tests need refactoring
**Effort:** Small
**Risk:** Low

### Option B: Make internal
Change from `public` to `internal`.

**Pros:** Still usable in tests
**Cons:** Doesn't fully fix encapsulation
**Effort:** Trivial
**Risk:** Very Low

## Recommended Action

Option A: Remove method

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs` (lines 34-41)
- `tests/Framework.Blobs.SshNet.Tests.Integration/SshBlobStorageTests.cs` (line 65)

## Acceptance Criteria

- [ ] Method removed or made internal
- [ ] Tests updated if needed

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Identified via architecture review | |

## Resources

- N/A
