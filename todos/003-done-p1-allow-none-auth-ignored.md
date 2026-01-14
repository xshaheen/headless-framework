---
status: done
priority: p1
issue_id: "003"
tags: [code-review, security, authentication, dotnet]
dependencies: []
---

# AllowNoneAuthentication Option Ignored

## Problem Statement

`SshBlobStorageOptions.AllowNoneAuthentication` is declared with documentation but **never checked** in `_BuildConnectionInfo()`. The code silently falls back to `NoneAuthenticationMethod` when no password/key is provided, regardless of the option value.

**Why it matters:** Users believe they've disabled none-authentication but it's still enabled, creating false security assumptions.

## Findings

### Location
- `/Users/xshaheen/Dev/framework/headless-framework/src/Framework.Blobs.SshNet/SshBlobStorageOptions.cs:26-30`
- `/Users/xshaheen/Dev/framework/headless-framework/src/Framework.Blobs.SshNet/SshBlobStorage.cs:1034-1037`

### Option Declaration (never read)
```csharp
/// <summary>
/// Allow none-authentication fallback. When false (default), throws if no password or private key is provided.
/// Set to true only if intentionally using passwordless authentication.
/// </summary>
public bool AllowNoneAuthentication { get; set; } = false;
```

### Actual Behavior (ignores option)
```csharp
if (authenticationMethods.Count == 0)
{
    // AllowNoneAuthentication is NEVER checked here!
    authenticationMethods.Add(new NoneAuthenticationMethod(username));
}
```

## Proposed Solutions

### Option A: Enforce the option (Recommended)
**Pros:** Matches documented behavior, security-conscious default
**Cons:** Breaking change for users relying on implicit none-auth
**Effort:** Small
**Risk:** Low

```csharp
if (authenticationMethods.Count == 0)
{
    if (!options.AllowNoneAuthentication)
    {
        throw new InvalidOperationException(
            "No authentication method configured. Provide password, private key, " +
            "or set AllowNoneAuthentication=true for passwordless auth.");
    }
    authenticationMethods.Add(new NoneAuthenticationMethod(username));
}
```

### Option B: Remove the option
**Pros:** No dead code, simpler API
**Cons:** Removes security control
**Effort:** Small
**Risk:** Medium - less secure

### Option C: Log warning but allow
**Pros:** Non-breaking, provides visibility
**Cons:** Doesn't enforce documented behavior
**Effort:** Small
**Risk:** Medium

## Recommended Action

Use Option A: Enforce the option. Throw `InvalidOperationException` when `AllowNoneAuthentication=false` and no auth method provided.

## Technical Details

### Affected Files
- `src/Framework.Blobs.SshNet/SshBlobStorageOptions.cs`
- `src/Framework.Blobs.SshNet/SshBlobStorage.cs`

### Breaking Change
Option A is technically breaking if users:
1. Provide no password/key
2. Rely on implicit `NoneAuthenticationMethod` fallback
3. Have `AllowNoneAuthentication=false` (default)

## Acceptance Criteria

- [x] `AllowNoneAuthentication` option is enforced in `_BuildConnectionInfo`
- [x] Exception thrown when option is false and no auth method provided
- [x] Unit test verifies the option behavior

## Work Log

| Date | Action | Outcome/Learning |
|------|--------|------------------|
| 2026-01-13 | Code review identified | Dead option confirmed |
| 2026-01-14 | Triage approved | Status: ready |

## Resources

- SSH none authentication: Used for anonymous access or when auth handled externally

### 2026-01-14 - Completed

**By:** Agent
**Actions:**
- Status changed: ready → done
