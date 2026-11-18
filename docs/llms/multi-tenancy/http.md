---
domain: Multi-Tenancy
packages:
---

# HTTP tenancy

> Part of [Multi-Tenancy](../multi-tenancy.md). Resolve the tenant from the access token's claim, require a tenant at the authorization boundary, opt endpoints out, and map tenant failures to ProblemDetails.

## HTTP Setup

`AddHeadless()` and `AddHeadlessDbContextServices()` register `CurrentTenant` as `ICurrentTenant`. Wire claim resolution and the tenant requirement:

```csharp
builder.AddHeadlessTenancy(tenancy =>
    tenancy
        .Http(http =>
            http.ResolveFromClaims(options =>
            {
                options.ClaimType = UserClaimTypes.TenantId; // default
            })
        )
        .Authorization(auth => auth.RequireTenant())
);

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new TenantRequirement())
        .Build();
});

app.UseAuthentication();
app.UseHeadlessTenancy();
app.UseAuthorization();
```

`.Http(http => http.ResolveFromClaims(...))` registers `CurrentTenant` and `ICurrentTenantAccessor` when missing, and configures `HttpTenantResolutionOptions`.

`.Authorization(auth => auth.RequireTenant())` registers the handler that fails `TenantRequirement` when no tenant is ambient. It does not add `TenantRequirement` to any policy: put it in `FallbackPolicy` or `DefaultPolicy` yourself, and in each named policy that must require a tenant. Startup fails with `HEADLESS_TENANCY_AUTHORIZATION_POLICY_MISSING` when neither `DefaultPolicy` nor `FallbackPolicy` includes it. Your own `IAuthorizationMiddlewareResultHandler` keeps working in any registration order, because the tenant rejection does not replace or decorate it.

`UseHeadlessTenancy()` adds the claim middleware only when `.Http(...)` configured claim resolution. Otherwise it does nothing. A second call adds nothing. It throws `InvalidOperationException` when `AddHeadlessTenancy(...)` was never called. When claim resolution is configured and `UseHeadlessTenancy()` is missing, startup fails with `HEADLESS_TENANCY_HTTP_MIDDLEWARE_MISSING`.

For each request, the claim middleware:

- Reads the claim named by `HttpTenantResolutionOptions.ClaimType` (default `tenant_id`).
- Calls `currentTenant.Change(tenantId)` only when the principal is authenticated and the claim is not blank.
- Restores the previous tenant when the request ends.

When an unauthenticated request reaches the middleware and authentication has not run for it, the middleware logs the `HEADLESS_TENANCY_MIDDLEWARE_ORDERING` warning, once per host. It usually means `UseHeadlessTenancy()` sits before `UseAuthentication()`.

**Verify:** an authenticated request whose token has `tenant_id = "t1"` sees `ICurrentTenant.Id == "t1"` in the endpoint. A request with no claim to an endpoint under the tenant-required policy returns 403 `g:tenant_required`. A host missing `UseHeadlessTenancy()` fails the `check` verb with `HEADLESS_TENANCY_HTTP_MIDDLEWARE_MISSING`.

## Skipping Tenant Resolution

Apply `[SkipTenantResolution]` or `.SkipTenantResolution()` to an endpoint, route group, MVC controller, or action to skip claim extraction. The middleware never calls `ICurrentTenant.Change(...)` for that request, so unless another resolver runs, `ICurrentTenant.Id` stays unset and `IsAvailable` stays false for the whole request.

The same marker skips catalog identifier resolution and its rejections: a skip-marked endpoint never answers with a tenant rejection, even for an unknown or ambiguous identifier.

The marker acts at the HTTP layer only. The EF guards, the messaging publish guard, and tenant-scoped blobs and caches still read `ICurrentTenant.Id`, so a handler under the marker that calls a tenant-required service still throws `MissingTenantContextException`.

```csharp
// Minimal API — single endpoint
app.MapGet("/health", () => Results.Ok()).SkipTenantResolution();

// Minimal API — route group
var publicGroup = app.MapGroup("/public").SkipTenantResolution();
publicGroup.MapGet("/status", () => Results.Ok());

// MVC — controller (applies to all actions)
[SkipTenantResolution]
[Route("admin")]
public sealed class AdminController : ControllerBase;

// MVC — individual action
[Route("users")]
public sealed class UsersController : ControllerBase
{
    [HttpGet("me")]
    [SkipTenantResolution]
    public IActionResult Profile() => Ok();
}
```

When the endpoint also sits under a tenant-required policy, add `.AllowMissingTenant()` so the requirement passes:

```csharp
app.MapGet("/webhook", (ICurrentTenant t) => Results.Ok()).SkipTenantResolution().AllowMissingTenant();
```

Choose `[SkipTenantResolution]` over `[AllowMissingTenant]` when the endpoint must not run under a tenant at all:

- The principal type never carries a tenant claim (service account, monitoring agent, webhook receiver).
- Claim extraction costs measurable time on a hot path that never needs a tenant.
- The endpoint is anonymous and you want to suppress the middleware-ordering warning.
- The endpoint resolves its tenant another way (a webhook signature, a path segment) and the claim middleware must not override it.

Choose `[AllowMissingTenant]` when callers may or may not carry a claim. It still extracts the claim and lets the requirement pass when there is none.

`UseHeadlessTenancy()` must run after routing, so the middleware sees endpoint metadata. Otherwise the skip marker has no effect and claim extraction runs anyway. `WebApplication` adds routing ahead of your middleware by default. If you call `UseRouting()` yourself, call it before `UseHeadlessTenancy()`.

**Verify:** a request whose token carries a tenant claim reaches the skip-marked endpoint with `ICurrentTenant.IsAvailable == false`.

## HTTP Failure Mapping

`MissingTenantContextException` is raised by every tenant-required path: the EF write and read guards, the messaging publish guard, the jobs enqueue guard, tenant-scoped blobs and caches, and your own code. `AddHeadlessProblemDetails()`, which `AddHeadless()` calls, registers the Headless exception handler, which maps it to a 403 ProblemDetails. The handler covers exceptions that reach ASP.NET Core's exception-handler middleware: MVC actions, Minimal API endpoints, and middleware after `UseExceptionHandler()`. Hosted services, SignalR hubs, and middleware before `UseExceptionHandler()` need their own catch.

```csharp
builder.AddHeadless();

// AddHeadless() calls AddHeadlessProblemDetails() which auto-registers
// HeadlessApiExceptionHandler. No opt-in needed.

var app = builder.Build();
app.UseExceptionHandler();
```

The response:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
  "title": "forbidden",
  "status": 403,
  "detail": "This operation requires a tenant, but none was set.",
  "error": {
    "code": "g:tenant_required",
    "description": "This operation requires a tenant, but none was set."
  },
  "traceId": "...",
  "buildNumber": "...",
  "commitNumber": "...",
  "instance": "/path",
  "timestamp": "..."
}
```

The body never includes the exception's `Message`, `Data`, or `InnerException`. Those go to server logs. Branch on `error.code`.

- Call `app.UseExceptionHandler()` yourself.
- The Headless handler wins over any catch-all registered after `AddHeadlessProblemDetails()`. To let your catch-all win, register it before `AddHeadlessProblemDetails()`, or before `AddHeadless()`.

To return the same body without throwing, for example from a pre-check that returns `Results.Problem(...)`, call `IProblemDetailsCreator.Forbidden(detail: error.Description, error: error)` with `var error = GeneralMessageDescriber.TenantRequired();` (namespace `Headless.Api.Resources`). Create the descriptor per response, so the description follows the request culture.

**Verify:** in a Production-environment test host, a request whose handler throws `MissingTenantContextException` returns 403 with `error.code` `g:tenant_required`.

## HTTP Authorization Requirement

`Headless.Api` enforces a tenant at the ASP.NET Core authorization boundary:

- Register it with `.Authorization(auth => auth.RequireTenant())`.
- Add `new TenantRequirement()` to `FallbackPolicy` or `DefaultPolicy`.
- A request passes when `ICurrentTenant.Id` is not blank.
- Mark host-level, public, system, and console-bootstrap endpoints with `[AllowMissingTenant]` or `.AllowMissingTenant()`.
- Use `[RequireTenant]` or `.RequireTenant()` to opt one endpoint or action back in under a group or controller marked `AllowMissingTenant`.
- Keep the order `UseAuthentication()`, `UseHeadlessTenancy()`, `UseAuthorization()`, so the requirement sees the resolved tenant.

### Limitations

- **Named policies are your job.** Startup validation checks only `DefaultPolicy` and `FallbackPolicy`. A `TenantRequirement` in a named policy does not satisfy it.
- **`[Authorize("NamedPolicy")]` bypasses `DefaultPolicy` and `FallbackPolicy`.** This is ASP.NET Core behavior. Add `TenantRequirement` to every named policy you apply, or also apply a policy that includes it. Headless cannot check this.
- **The `g:tenant_required` body needs the status-codes rewriter.** `UseHeadless()` from `Headless.Api.ServiceDefaults` adds it. Without ServiceDefaults, call `app.UseStatusCodesRewriter()` yourself, or a failed requirement returns a bare 403 with no `error.code`.
- **`[AllowAnonymous]` skips authorization**, so `TenantRequirement` never runs. A handler there that reads a tenant-required service throws `MissingTenantContextException`, which still maps to 403 `g:tenant_required`. Keep anonymous endpoints away from tenant state. Use `[AllowMissingTenant]` only when you want the authorization opt-out.

Apply `[AllowMissingTenant]` or `.AllowMissingTenant()` to every endpoint that can run without a tenant:

- **Anonymous and public endpoints:** sign-in, password reset, sign-up, public lookups.
- **Admin, system, and console-bootstrap endpoints** that run under a host-level identity.
- **Authenticated endpoints reached by principals with no tenant claim:** admin, partner, service-account, and cross-tenant principals.

A missing opt-out returns 403 `g:tenant_required` to legitimate callers. Leave the opt-out off endpoints that need a tenant.

```csharp
builder.AddHeadlessTenancy(tenancy =>
    tenancy.Http(http => http.ResolveFromClaims()).Authorization(auth => auth.RequireTenant())
);

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new TenantRequirement())
        .Build();
});

var publicGroup = app.MapGroup("/public").AllowMissingTenant();
publicGroup.MapGet("/status", () => Results.Ok());
publicGroup.MapGet("/tenant-data", () => Results.Ok()).RequireTenant();

[AllowMissingTenant]
public sealed class PublicBootstrapController : ControllerBase
{
    [RequireTenant]
    public IActionResult TenantScopedAction() => Ok();
}
```

A request with no tenant fails the requirement with the `TenantContextRequired` reason. ASP.NET Core writes a bare 403, and the status-codes rewriter replaces it with the `g:tenant_required` body shown in [HTTP Failure Mapping](#http-failure-mapping). Other 403 responses pass through the rewriter without the `error` discriminator.

**Verify:** an authenticated request with no tenant claim gets 403 `g:tenant_required` from `/tenant-data` and 200 from `/status`. A host without `TenantRequirement` in `DefaultPolicy` or `FallbackPolicy` fails the `check` verb with `HEADLESS_TENANCY_AUTHORIZATION_POLICY_MISSING`.
