---
status: pending
priority: p1
issue_id: "027"
tags: [code-review, security, csrf, api-demo]
dependencies: []
---

# Missing CSRF Protection on State-Changing Endpoints

## Problem Statement

State-changing endpoints (POST, DELETE) lack CSRF protection. Attackers can trick authenticated administrators into granting permissions via malicious websites, exploiting browser cookie-based authentication.

**Impact:** Cross-site request forgery enables unauthorized permission grants when admin visits attacker-controlled page.

## Findings

**Location:** `demo/Framework.Permissions.Api.Demo/Controllers/PermissionsController.cs:57-71,73-96,98-111`

**Vulnerable Endpoints:**
- `POST /api/permissions/grants` - No `[ValidateAntiForgeryToken]`
- `DELETE /api/permissions/grants` - No CSRF protection
- `DELETE /api/permissions/grants/{provider}/{key}` - No CSRF protection

**Attack Scenario:**
```html
<!-- Attacker's malicious page -->
<form action="https://victim-api.com/api/permissions/grants" method="POST">
  <input type="hidden" name="name" value="Admin.Full" />
  <input type="hidden" name="providerName" value="User" />
  <input type="hidden" name="providerKey" value="attacker-id" />
</form>
<script>
  document.forms[0].submit();
</script>
```

When authenticated admin visits this page, permissions are granted to attacker.

**Source:** security-sentinel agent review

## Proposed Solutions

### Option A: Antiforgery Tokens for Cookie Auth
**Pros:** Framework built-in, standard ASP.NET Core pattern
**Cons:** Only works with cookie auth, adds header complexity
**Effort:** Small
**Risk:** Low

```csharp
// Program.cs
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

// Controller
[HttpPost("grants")]
[Authorize(Policy = "PermissionsManage")]
[ValidateAntiForgeryToken]
public async Task<IActionResult> GrantPermission(...)
```

### Option B: SameSite Cookies Only (JWT Bearer)
**Pros:** Simple if using JWT Bearer tokens
**Cons:** Doesn't protect against all CSRF variants
**Effort:** Minimal
**Risk:** Medium

```csharp
options.Cookie.SameSite = SameSiteMode.Strict;
```

### Option C: Custom Request Validation
**Pros:** Full control, API-specific
**Cons:** Reinventing the wheel, error-prone
**Effort:** Medium
**Risk:** High

## Recommended Action

<!-- Fill during triage -->

## Technical Details

**Affected files:**
- `demo/Framework.Permissions.Api.Demo/Program.cs` (add antiforgery configuration)
- `demo/Framework.Permissions.Api.Demo/Controllers/PermissionsController.cs` (add attributes)

**Context:** Demo currently uses no authentication scheme (see todo/024), so CSRF risk depends on auth implementation.

**If JWT Bearer:** CSRF protection automatic (tokens in Authorization header, not cookies)
**If Cookie Auth:** Requires antiforgery tokens

**OWASP Mapping:** A01:2021 - Broken Access Control

## Acceptance Criteria

- [ ] Antiforgery service configured (if cookie auth)
- [ ] `[ValidateAntiForgeryToken]` on POST/DELETE endpoints
- [ ] CSRF attack attempt returns 400 Bad Request
- [ ] README documents CSRF token usage
- [ ] Integration tests verify CSRF protection

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-15 | Created from code review | Security Sentinel identified CSRF vulnerability |

## Resources

- Security Sentinel findings
- ASP.NET Core CSRF: https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery
- OWASP CSRF: https://owasp.org/www-community/attacks/csrf
