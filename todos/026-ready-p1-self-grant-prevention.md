---
status: ready
priority: p1
issue_id: "026"
tags: [code-review, security, privilege-escalation, api-demo]
dependencies: []
---

# Privilege Escalation - No Self-Grant Prevention

## Problem Statement

The `GrantPermission` endpoint allows administrators to grant permissions to themselves, enabling privilege escalation attacks. No validation prevents users from modifying their own permission set, violating separation of duties principles.

**Impact:** Admin can grant themselves unlimited privileges, bypass audit controls, escalate to super-admin.

## Findings

**Location:** `demo/Framework.Permissions.Api.Demo/Controllers/PermissionsController.cs:57-71`

```csharp
[HttpPost("grants")]
[Authorize(Policy = "PermissionsManage")]
public async Task<IActionResult> GrantPermission(
    [FromBody] GrantPermissionRequest request, ...)
{
    await permissionManager.SetAsync(
        request.Name,
        request.ProviderName,
        request.ProviderKey,  // No check if this is current user!
        isGranted: true,
        cancellationToken);
}
```

**Attack Scenario:**
```bash
# Authenticated admin grants themselves super-admin rights
POST /api/permissions/grants
Authorization: Bearer <admin-token>
{
  "name": "SuperAdmin.All",
  "providerName": "User",
  "providerKey": "current-admin-user-id"
}
# Returns 204 - grant succeeds
```

**Security Violations:**
- Privilege escalation
- Audit trail circumvention (self-granted permissions are untraceable)
- Separation of duties violation
- No four-eyes principle

**Source:** security-sentinel agent review

## Proposed Solutions

### Option A: Prevent Self-Granting (Recommended)
**Pros:** Simple check, prevents most privilege escalation
**Cons:** Doesn't prevent granting higher privileges
**Effort:** Small
**Risk:** Low

```csharp
[HttpPost("grants")]
public async Task<IActionResult> GrantPermission(...)
{
    if (request.ProviderName == "User" &&
        request.ProviderKey == currentUser.UserId?.Value)
    {
        return ConflictProblemDetails(new ErrorDescriptor(
            "SelfGrantProhibited",
            "Cannot grant permissions to yourself"));
    }
    // ... proceed with grant
}
```

### Option B: Validate Permission Hierarchy
**Pros:** Prevents granting higher privileges than user possesses
**Cons:** More complex, requires permission hierarchy check
**Effort:** Medium
**Risk:** Low

```csharp
var currentUserPermissions = await permissionManager.GetAllAsync(
    currentUser, cancellationToken: cancellationToken);

if (!currentUserPermissions.Any(p => p.Name == request.Name && p.IsGranted))
{
    return ConflictProblemDetails(new ErrorDescriptor(
        "InsufficientPrivileges",
        "Cannot grant permissions you do not possess"));
}
```

### Option C: Require Second Approver
**Pros:** Industry best practice (four-eyes principle)
**Cons:** Requires workflow system, complex
**Effort:** Large
**Risk:** Medium

## Recommended Action

**Option A: Prevent Self-Granting** - IMPLEMENTED

## Technical Details

**Affected files:**
- `demo/Framework.Permissions.Api.Demo/Controllers/PermissionsController.cs:57-71`

**Required context:**
- Access to `ICurrentUser` (already injected in controller)
- `currentUser.UserId` for identity comparison

**Edge cases to handle:**
- Role-based grants (ProviderName == "Role") might be acceptable
- System user grants (automated processes)

**OWASP Mapping:** A01:2021 - Broken Access Control

## Acceptance Criteria

- [x] Self-grant attempts return 409 Conflict with descriptive error
- [ ] Integration test verifies self-grant rejection
- [ ] Audit log records self-grant attempts
- [x] Role-based grants evaluated separately (may allow)
- [x] README documents self-grant prevention policy

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-15 | Created from code review | Security Sentinel identified privilege escalation risk |
| 2026-01-15 | Implemented self-grant prevention | Added validation in POST /api/permissions/grants endpoint checking if providerName == "User" && providerKey == currentUser.UserId. Returns 409 Conflict with SelfGrantProhibited error code. Updated README with security policy section. |

## Resources

- Security Sentinel review findings
- OWASP Access Control: https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html
