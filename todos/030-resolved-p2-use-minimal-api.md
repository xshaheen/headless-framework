---
status: completed
priority: p2
issue_id: "030"
tags: [code-review, architecture, minimal-api, api-demo, user-feedback]
dependencies: []
---

# Convert Demo to Use Minimal API Instead of MVC Controllers

## Problem Statement

Demo currently uses MVC controllers pattern (`ApiControllerBase`, `[ApiController]` attributes) when Minimal API would be more appropriate for a simple REST API demo. User explicitly requested "use minimal api".

**Impact:** Demo shows outdated pattern, adds unnecessary ceremony for simple API, doesn't demonstrate modern .NET approach.

## Findings

**Current Implementation:**
- Uses `Framework.Api.Mvc` package dependency
- Extends `ApiControllerBase` from MVC stack
- Requires Controllers folder structure
- More boilerplate than necessary

**Minimal API Benefits:**
- Less code, more direct
- Better performance (no MVC overhead)
- Simpler for demos
- Modern .NET 6+ pattern
- Still supports OpenAPI, authorization, validation

**Source:** User feedback + Pragmatic .NET Reviewer (Scott Hanselman would ask "but why though?" about controller overhead)

## Proposed Solutions

### Option A: Full Minimal API Conversion (Recommended)
**Pros:** Modern pattern, less code, better performance, user request
**Cons:** Different from existing controller-based demos
**Effort:** Medium
**Risk:** Low

```csharp
// Program.cs
var app = builder.Build();

app.MapGet("/api/permissions", async (
    IPermissionDefinitionManager definitionManager,
    CancellationToken ct) =>
{
    var permissions = await definitionManager.GetPermissionsAsync(ct);
    return Results.Ok(permissions);
}).RequireAuthorization();

app.MapGet("/api/permissions/{name}", async (
    string name,
    IPermissionDefinitionManager definitionManager,
    CancellationToken ct) =>
{
    var permission = await definitionManager.FindAsync(name, ct);
    return permission is null
        ? Results.NotFound()
        : Results.Ok(permission);
}).RequireAuthorization();

app.MapGet("/api/permissions/check", async (
    [FromQuery] string[] names,
    IPermissionManager permissionManager,
    ICurrentUser currentUser,
    CancellationToken ct) =>
{
    if (names.Length == 0) return Results.BadRequest();

    var results = await permissionManager.GetAllAsync(names, currentUser, cancellationToken: ct);
    return Results.Ok(results);
}).RequireAuthorization();

app.MapPost("/api/permissions/grants", async (
    GrantPermissionRequest request,
    IPermissionManager permissionManager,
    CancellationToken ct) =>
{
    await permissionManager.SetAsync(
        request.Name, request.ProviderName, request.ProviderKey,
        isGranted: true, ct);
    return Results.NoContent();
}).RequireAuthorization("PermissionsManage");

// ... DELETE endpoints
```

### Option B: Hybrid Approach
**Pros:** Show both patterns
**Cons:** Inconsistent, confusing
**Effort:** Medium
**Risk:** Medium

### Option C: Keep Controllers, Document Why
**Pros:** Consistent with other demos
**Cons:** Ignores user feedback, less modern
**Effort:** Minimal
**Risk:** Low (but not recommended)

## Recommended Action

<!-- Fill during triage -->

## Technical Details

**Files to modify:**
- Delete: `demo/Framework.Permissions.Api.Demo/Controllers/PermissionsController.cs`
- Delete: `demo/Framework.Permissions.Api.Demo/Controllers/` folder
- Modify: `demo/Framework.Permissions.Api.Demo/Program.cs` (add endpoints)
- Keep: `demo/Framework.Permissions.Api.Demo/Models/GrantPermissionRequest.cs` (still needed)
- Modify: `.csproj` (remove Framework.Api.Mvc reference, keep Framework.Api if needed)

**Benefits:**
- Removes ~113 lines of controller code
- Replaces with ~50 lines of endpoint mappings
- Better performance (no MVC routing overhead)
- Shows modern .NET pattern
- Still supports all features (auth, validation, OpenAPI)

**Considerations:**
- Need to handle model validation differently (use filters or manual validation)
- Problem Details still work with Results API
- Authorization via `.RequireAuthorization(policy)`

## Acceptance Criteria

- [x] All controller endpoints converted to Minimal API
- [x] Authorization still enforced (RequireAuthorization)
- [x] Model validation works correctly
- [x] OpenAPI generation still functional
- [x] README updated with Minimal API examples
- [x] All tests pass
- [x] No dependency on Framework.Api.Mvc

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-15 | Created from user feedback | User explicitly requested Minimal API |
| 2026-01-15 | Completed conversion | Reduced from 145 lines (controller + program) to 117 lines (program only), ~19% reduction. Removed MVC dependencies. |

## Resources

- User request: "use minimal api"
- Minimal API docs: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis
- Pragmatic .NET Reviewer findings
