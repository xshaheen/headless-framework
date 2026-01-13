---
status: completed
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

### Current Implementation (Fixed)
- **File:** `src/Framework.Blobs.SshNet/SshBlobStorage.cs:986-987`
```csharp
// NOW PROPERLY DECODED - MATCHES MAIN CREDENTIALS PATTERN
var proxyUsername = Uri.UnescapeDataString(proxyParts[0]);
var proxyPassword = proxyParts.Length > 1 ? Uri.UnescapeDataString(proxyParts[1]) : null;
```

Matches main credentials pattern (lines 936-937):
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

Applied Uri.UnescapeDataString() to both proxy username and password to match the main credentials URL-decode pattern. This ensures proxy authentication works correctly with special characters in credentials.

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs` (lines 986-987)

## Acceptance Criteria

- [x] Proxy credentials with special characters work correctly
- [x] URL-encoded characters in proxy password are properly decoded
- [x] Code matches main credentials URL-decode pattern

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-13 | Identified via security review | Copy-paste bug - main creds handled correctly |
| 2026-01-13 | Resolved - Uri.UnescapeDataString applied to proxy credentials | Fix committed in 6a2101fa |

## Resources

- URI.UnescapeDataString docs: https://learn.microsoft.com/en-us/dotnet/api/system.uri.unescapedatastring
