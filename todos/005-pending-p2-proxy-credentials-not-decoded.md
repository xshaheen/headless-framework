---
status: pending
priority: p2
issue_id: "005"
tags: [code-review, security, bug]
dependencies: []
---

# Proxy Credentials Not URL-Decoded

## Problem Statement

Proxy credentials extracted from URI are NOT URL-decoded, unlike main connection credentials. This causes authentication failures when special characters exist in proxy passwords.

**Why it matters:** Inconsistent behavior between main and proxy authentication; proxy auth will fail with special characters.

## Findings

### From security-sentinel:
- **File:** `src/Framework.Blobs.SshNet/SshBlobStorage.cs:912-914`
```csharp
// NO Uri.UnescapeDataString() for proxy
var proxyUsername = proxyParts[0];  // NOT DECODED
var proxyPassword = proxyParts.Length > 1 ? proxyParts[1] : null;  // NOT DECODED
```

Compare with main credentials (lines 874-875) - PROPERLY DECODED:
```csharp
var username = Uri.UnescapeDataString(userParts[0]);
var password = Uri.UnescapeDataString(userParts.Length > 1 ? userParts[1] : string.Empty);
```

## Proposed Solutions

### Option A: Apply UnescapeDataString (Recommended)
```csharp
var proxyUsername = Uri.UnescapeDataString(proxyParts[0]);
var proxyPassword = proxyParts.Length > 1 ? Uri.UnescapeDataString(proxyParts[1]) : null;
```

**Pros:** Consistent with main credential handling
**Cons:** None
**Effort:** Trivial
**Risk:** Very Low

## Recommended Action

<!-- Fill after triage -->

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs` (lines 912-914)

## Acceptance Criteria

- [ ] Proxy credentials with special characters work correctly
- [ ] URL-encoded characters in proxy password are properly decoded
- [ ] Unit test covers this scenario

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Identified via security review | Copy-paste bug - main creds handled correctly |

## Resources

- URI.UnescapeDataString docs: https://learn.microsoft.com/en-us/dotnet/api/system.uri.unescapedatastring
