---
status: pending
priority: p3
issue_id: "017"
tags: [code-review, security, configuration]
dependencies: []
---

# Review NoneAuthenticationMethod Fallback

## Problem Statement

When no password or private key is provided, the code silently falls back to `NoneAuthenticationMethod`. This may lead to failed connections or unintended anonymous access.

**Why it matters:** Configuration errors should fail loudly, not silently.

## Findings

### From security-sentinel:
- **File:** `src/Framework.Blobs.SshNet/SshBlobStorage.cs:895-898`
```csharp
if (authenticationMethods.Count == 0)
{
    authenticationMethods.Add(new NoneAuthenticationMethod(username));
}
```

## Proposed Solutions

### Option A: Throw exception (Recommended)
```csharp
if (authenticationMethods.Count == 0)
{
    throw new InvalidOperationException(
        "No authentication method configured. Provide password or private key.");
}
```

**Pros:** Fails fast, clear error
**Cons:** Breaking if anyone relies on None auth
**Effort:** Trivial
**Risk:** Low

### Option B: Add explicit config option
Add `AllowNoneAuthentication` option.

**Pros:** Explicit intent
**Cons:** More options
**Effort:** Small
**Risk:** Low

## Recommended Action

<!-- Fill after triage -->

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs` (lines 895-898)

## Acceptance Criteria

- [ ] Missing credentials cause clear error
- [ ] Intentional none-auth still possible if needed

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Identified via security review | |

## Resources

- SSH authentication methods
