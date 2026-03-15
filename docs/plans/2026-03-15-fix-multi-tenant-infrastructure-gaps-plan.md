---
title: "fix: Multi-tenant infrastructure gaps in Headless Framework"
type: fix
date: 2026-03-15
---

> **Verification gate:** Before claiming any task or story complete — run the plan's `verification_command` and confirm PASS. Do not mark complete based on reading code alone.

# fix: Multi-tenant infrastructure gaps in Headless Framework

## Overview

The Headless Framework has all multi-tenant building blocks (`ICurrentTenant`, `IMultiTenant`, EF global query filters, `AsyncLocalCurrentTenantAccessor`, `ScopedCache<T>`) but lacks the glue to make them usable. Apps must manually wire several pieces together — error-prone and repetitive.

This plan addresses [headless-framework#188](https://github.com/xshaheen/headless-framework/issues/188).

## Problem Statement

### 1. Default `ICurrentTenant` is `NullCurrentTenant`

`ApiSetup.AddHeadlessApi()` registers `NullCurrentTenant` — a no-op implementation where `Change()` does nothing. Apps wanting multi-tenancy must know to override this with `CurrentTenant` using `AddOrReplace`, which is easy to miss since `TryAdd` won't replace.

**Impact:** EF query filters for `IMultiTenant` entities compare `TenantId == null`. Permission `ScopedCache` scope resolves to `t:` (null tenant) — all tenants share one cache partition.

**Location:** `src/Headless.Api/Setup.cs` — `TryAddSingleton<ICurrentTenant, NullCurrentTenant>()`

### 2. No middleware for claim-based tenant resolution

All pieces exist but aren't connected:

- `UserClaimTypes.TenantId = "tenant_id"` — defined
- `ClaimsPrincipal.GetTenantId()` — extension exists
- `ICurrentTenant.Change()` — works on `CurrentTenant`
- **Middleware to read claim → set tenant** — missing

Every multi-tenant app needs to write the same ~15 lines of boilerplate.

### 3. No helper DI method to opt into multi-tenancy

No single method exists to enable multi-tenancy. Apps need to know about `CurrentTenant`, `AsyncLocalCurrentTenantAccessor`, claim types, and middleware ordering.

### 4. Permission cache keys — already fixed ✅

`Permissions.Core/Setup.cs` already registers `ICache<PermissionGrantCacheItem>` as `ScopedCache` with tenant scope provider:

```csharp
services.AddSingleton<ICache<PermissionGrantCacheItem>>(sp =>
    new ScopedCache<PermissionGrantCacheItem>(
        sp.GetRequiredService<ICache>(),
        () => $"t:{sp.GetRequiredService<ICurrentTenant>().Id}"
    )
);
```

**However**, this only works when #1 is fixed — `NullCurrentTenant.Id` is always `null`, so all tenants share scope `t:`.

### 5. No documentation for multi-tenant setup

No guidance on end-to-end setup, middleware ordering, entity configuration, or permission integration.

## Proposed Solution

| # | Change | Package | Breaking? |
|---|--------|---------|-----------|
| 1 | Register `CurrentTenant` instead of `NullCurrentTenant` | `Headless.Api` | No — same behavior when tenant not set |
| 2 | Add `UseHeadlessTenantResolution()` middleware | `Headless.Api` | No — opt-in |
| 3 | Add `AddHeadlessMultiTenancy()` helper | `Headless.Api` | No — opt-in |
| 4 | ~~Fix permission cache~~ Already done | — | — |
| 5 | Add `docs/llms/multi-tenancy.txt` | docs | No |

### Fix #1: Default registration

```csharp
// Before
builder.Services.TryAddSingleton<ICurrentTenant, NullCurrentTenant>();

// After
builder.Services.TryAddSingleton<ICurrentTenant, CurrentTenant>();
```

`CurrentTenant` reads from `AsyncLocalCurrentTenantAccessor` which defaults to `null` — identical behavior to `NullCurrentTenant` when no tenant is set, but works correctly when `Change()` is called.

### Fix #2: Tenant resolution middleware

```csharp
// TenantResolutionMiddleware.cs
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICurrentTenant currentTenant)
    {
        var tenantId = context.User.GetTenantId();

        if (tenantId is not null)
        {
            using var _ = currentTenant.Change(tenantId);
            await next(context);
        }
        else
        {
            await next(context);
        }
    }
}
```

**Pipeline placement:** After `UseAuthentication()`, before `UseAuthorization()`.

**Edge cases:**
- Unauthenticated requests → `GetTenantId()` returns `null` → no tenant set → host-level context
- Anonymous endpoints → same as unauthenticated
- Missing `tenant_id` claim on authenticated user → no tenant set → host-level (intentional for super-admin scenarios)

### Fix #3: DI helper

```csharp
public static class MultiTenancySetup
{
    public static IHostApplicationBuilder AddHeadlessMultiTenancy(
        this IHostApplicationBuilder builder,
        Action<MultiTenancyOptions>? configure = null)
    {
        var options = new MultiTenancyOptions();
        configure?.Invoke(options);

        // Ensure CurrentTenant (not NullCurrentTenant)
        builder.Services.AddOrReplace<ICurrentTenant, CurrentTenant>(ServiceLifetime.Singleton);

        // Store options for middleware
        builder.Services.AddSingleton(options);

        return builder;
    }
}

public sealed class MultiTenancyOptions
{
    /// <summary>Claim type to read tenant ID from. Defaults to "tenant_id".</summary>
    public string ClaimType { get; set; } = UserClaimTypes.TenantId;
}
```

## Critical Design Decisions

### D1: Null tenant handling in middleware

When `GetTenantId()` returns null (unauthenticated, anonymous endpoints, platform admin without tenant claim):

**Decision: Skip `Change()` entirely.** Leave `ICurrentTenantAccessor.Current` as `null` (never set).

Rationale: `ICurrentTenantAccessor` distinguishes between `Current == null` (never set) and `Current.TenantId == null` (explicitly set to null). Calling `Change(null)` would create a `TenantInformation(null)` — semantically "explicitly host context" — which could behave differently in EF filters and cache. Skipping preserves backward compatibility.

For endpoints that *require* tenant context, apps should add a `[RequiresTenant]` endpoint filter that returns 403 when `ICurrentTenant.IsAvailable == false`. This is app-level, not framework-level.

### D2: Subtle breaking change in #1

`NullCurrentTenant.Change()` returns `DisposableFactory.Empty` — a complete no-op. `CurrentTenant.Change()` actually mutates `AsyncLocal`. Code that calls `Change()` on what was previously a no-op will now actually set tenant state.

**Mitigated by:** `TryAddSingleton` semantics — any app that already explicitly registered `NullCurrentTenant` or `CurrentTenant` is unaffected. The change only affects apps relying on the framework's default registration where `Change()` was silently a no-op.

### D3: Message consumer and background job tenant resolution — out of scope

The messaging framework supports `TenantId` headers but has no automatic tenant resolution in the consumer pipeline. Background jobs also need manual `Change()` calls.

**Decision: Document patterns only (US-004).** Automatic propagation via `IConsumeFilter` is a separate issue — it could be surprising for consumers that intentionally operate cross-tenant. The docs should cover:
- Background job tenant iteration pattern (`foreach tenant → using Change()`)
- Message consumer header-based pattern

### D4: SignalR — out of scope

SignalR middleware runs only once (on WebSocket upgrade). Hub invocations start new execution contexts where `AsyncLocal` won't have tenant state. Each hub method invocation would need its own tenant resolution. This is a separate concern from HTTP middleware.

## Technical Considerations

- **Non-HTTP contexts (background jobs, message handlers):** These already call `ICurrentTenant.Change()` directly (e.g., `PermissionGrantCacheItemInvalidator`). The middleware only handles the HTTP path.
- **`ICurrentTenant` lifetime:** Registered as singleton. `AsyncLocalCurrentTenantAccessor` uses `AsyncLocal<T>` for per-request isolation — safe for concurrent requests. Kestrel creates new execution contexts per request, so `AsyncLocal` does not leak between requests.
- **`Change()` returns `IDisposable`:** The middleware must `using` the disposable to restore previous tenant on scope exit. Disposing after `next()` returns ensures tenant context is available for the entire request pipeline including response formatting.
- **Middleware ordering is critical:** Must run after authentication (needs `ClaimsPrincipal`) and before authorization (permission checks need tenant context). Consider a debug-only ordering validation in `UseHeadlessTenantResolution()`.
- **Tenant validation:** The middleware does NOT validate that `tenant_id` refers to an actual, active tenant. It trusts the JWT claim. Tenant existence validation is an app-level concern (JWT should only contain valid tenant IDs if the token issuance logic is correct).
- **Cache safety for null tenant:** `ScopedCache` scope `"t:"` when Id is null creates a shared bucket. This is acceptable for host-level operations but should be documented. Platform admins temporarily switching tenants via `Change()` will get properly scoped cache keys.

## System-Wide Impact

- **Interaction graph:** `UseHeadlessTenantResolution()` → reads `ClaimsPrincipal` → calls `ICurrentTenant.Change()` → sets `AsyncLocal` → all downstream services (`HeadlessDbContext`, `PermissionGrantStore`, `ScopedCache<T>`) read tenant from `ICurrentTenant.Id`.
- **Error propagation:** If `GetTenantId()` returns null, the request proceeds as host-level. No errors thrown — intentional for super-admin and anonymous scenarios.
- **State lifecycle risks:** `Change()` returns `IDisposable` that restores previous tenant. If middleware doesn't `using` it, tenant could bleed to next request on the same `AsyncLocal` context. The middleware design above handles this correctly.
- **API surface parity:** Only HTTP resolution is added. Background jobs, message consumers, and SignalR hubs are out of scope — they resolve tenant through their own mechanisms.

## Stories

> Full story details in companion PRD: [`2026-03-15-fix-multi-tenant-infrastructure-gaps-plan.prd.json`](./2026-03-15-fix-multi-tenant-infrastructure-gaps-plan.prd.json)

| ID | Title | Size |
|----|-------|------|
| US-001 | Fix default ICurrentTenant registration | XS |
| US-002 | Add claim-based tenant resolution middleware | S |
| US-003 | Add AddHeadlessMultiTenancy() DI helper | S |
| US-004 | Add multi-tenancy documentation | S |

## Success Metrics

- `ICurrentTenant` resolves to `CurrentTenant` by default across all apps using `AddHeadlessApi()`
- Multi-tenant apps can set up tenant resolution with 2 lines: `AddHeadlessMultiTenancy()` + `UseHeadlessTenantResolution()`
- Permission cache isolation works correctly when tenant is set (via existing `ScopedCache` registration)
- `docs/llms/multi-tenancy.txt` covers end-to-end setup

## Dependencies & Risks

| Risk | Mitigation |
|------|------------|
| `NullCurrentTenant.Change()` is no-op → `CurrentTenant.Change()` mutates `AsyncLocal` | `TryAddSingleton` semantics protect apps with explicit registrations. Only affects apps relying on default where `Change()` was silently ignored. (See decision D2) |
| Middleware ordering mistakes by consumers | Documentation + debug-only ordering validation in `UseHeadlessTenantResolution()` |
| `ScopedCache` scope `t:` when no tenant set | Documented as expected host-level behavior. Platform admins using `Change()` for cross-tenant ops get proper scoping. |
| Integration tests relied on `NullCurrentTenant` default | `CurrentTenant` with no `Change()` call returns null for `Id` and false for `IsAvailable` — same as `NullCurrentTenant`. Tests using `TestCurrentTenant` explicitly are unaffected. |
| SignalR hub invocations lose tenant context after WebSocket upgrade | Out of scope — documented as known limitation. Hub-level tenant resolution needs a separate `IHubFilter`. |

## Sources & References

- Issue: [headless-framework#188](https://github.com/xshaheen/headless-framework/issues/188)
- `src/Headless.Api/Setup.cs` — `NullCurrentTenant` registration
- `src/Headless.Core/Abstractions/ICurrentTenant.cs` — `ICurrentTenant`, `CurrentTenant`, `NullCurrentTenant`
- `src/Headless.Extensions/Security/ClaimsPrincipalExtensions.cs` — `GetTenantId()`
- `src/Headless.Permissions.Core/Setup.cs` — `ScopedCache` registration (already fixed)
- `src/Headless.Caching.Abstractions/ScopedCache.cs` — `ScopedCache<T>` implementation
- `src/Headless.Orm.EntityFramework/Contexts/HeadlessEntityModelProcessor.cs` — EF global query filters
