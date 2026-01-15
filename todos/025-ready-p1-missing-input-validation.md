---
status: ready
priority: p1
issue_id: "025"
tags: [code-review, security, validation, api-demo]
dependencies: []
---

# Missing Input Validation - Request Models & Endpoints

## Problem Statement

Request models and controller endpoints lack comprehensive input validation, allowing empty strings, excessively long inputs, and special characters that could cause injection attacks, memory exhaustion, or data integrity issues.

**Impact:** DoS via oversized payloads, data corruption, potential injection attacks downstream.

## Findings

**Location:** `demo/Framework.Permissions.Api.Demo/Models/GrantPermissionRequest.cs`

```csharp
public sealed class GrantPermissionRequest
{
    public required string Name { get; init; }  // No length limit, no pattern
    public required string ProviderName { get; init; }
    public required string ProviderKey { get; init; }
}
```

**Issues:**
1. No `[StringLength]` attributes - allows unlimited input
2. No `[RegularExpression]` - allows any characters including SQL injection patterns
3. No `[Required]` - `required` keyword doesn't prevent empty strings
4. Inconsistent validation across endpoints (POST has none, DELETE validates manually)

**Attack Vectors:**
- Memory exhaustion: `{"name":"X".repeat(1000000),...}`
- Empty string bypass: `{"name":"","providerName":"","providerKey":""}`
- Special characters: `{"name":"'; DROP TABLE--",...}`

**Source:** strict-dotnet-reviewer, security-sentinel, pattern-recognition-specialist

## Proposed Solutions

### Option A: Data Annotations (Recommended)
**Pros:** Built-in framework support, declarative, minimal code
**Cons:** Limited to simple rules
**Effort:** Small
**Risk:** Low

```csharp
public sealed class GrantPermissionRequest
{
    [Required]
    [StringLength(256, MinimumLength = 1)]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$")]
    public required string Name { get; init; }

    [Required]
    [StringLength(128, MinimumLength = 1)]
    [RegularExpression(@"^[a-zA-Z0-9_-]+$")]
    public required string ProviderName { get; init; }

    [Required]
    [StringLength(256, MinimumLength = 1)]
    public required string ProviderKey { get; init; }
}
```

### Option B: FluentValidation
**Pros:** More expressive, complex rules, better error messages
**Cons:** Additional dependency (already in framework)
**Effort:** Medium
**Risk:** Low

```csharp
public class GrantPermissionRequestValidator : AbstractValidator<GrantPermissionRequest>
{
    public GrantPermissionRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ProviderName).NotEmpty().MaximumLength(128);
        RuleFor(x => x.ProviderKey).NotEmpty().MaximumLength(256);
    }
}
```

### Option C: Manual Validation in Controller
**Pros:** Full control
**Cons:** Violates DRY, error-prone, inconsistent
**Effort:** Medium
**Risk:** Medium

## Recommended Action

<!-- Fill during triage -->

## Technical Details

**Affected files:**
- `demo/Framework.Permissions.Api.Demo/Models/GrantPermissionRequest.cs`
- `demo/Framework.Permissions.Api.Demo/Controllers/PermissionsController.cs:28-36` (GetDefinition)
- `demo/Framework.Permissions.Api.Demo/Controllers/PermissionsController.cs:57-71` (GrantPermission)
- `demo/Framework.Permissions.Api.Demo/Controllers/PermissionsController.cs:98-111` (RevokeAllPermissions)

**Also fix:** Inconsistent validation across endpoints (lines 73-96 validate manually, others don't)

**OWASP Mapping:** A03:2021 - Injection, A04:2021 - Insecure Design

## Acceptance Criteria

- [ ] All request models have validation attributes
- [ ] String length limits enforced (prevent DoS)
- [ ] Pattern validation prevents injection characters
- [ ] Empty string validation consistent across all endpoints
- [ ] Controller methods return 400 with field-level error details
- [ ] Integration tests verify validation behavior

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-15 | Created from code review | Multiple agents identified validation gaps |
| 2026-01-15 | Resolved - Added Data Annotations validation | Implemented Option A with comprehensive validation attributes, updated README with security documentation |

## Resources

- Strict .NET Reviewer findings
- Security Sentinel report
- ASP.NET Core Model Validation: https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation
