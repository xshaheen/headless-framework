---
date: 2026-05-18
topic: tenancy-http-authorization-requirement
---

# Tenancy: HTTP Authorization Requirement (replace Mediator-pipeline gate)

## Summary

Replace the Mediator-pipeline tenant gate with a first-class ASP.NET authorization primitive — `TenantRequirement` — that consumers wire into `FallbackPolicy` / `DefaultPolicy`, plus an endpoint-level `[AllowMissingTenant]` opt-out. The old Mediator behavior, attribute, builder method, and the `HeadlessMediatorTenancyBuilder` shell are deleted outright (greenfield, no deprecation cycle). New pieces live in `Headless.Api.Core`.

---

## Problem Frame

`TenantRequiredBehavior<,>` enforces tenant presence inside the Mediator pipeline — after model binding, validation, and dispatch. Three things are wrong with that location:

1. **Late enforcement.** Abusive or malformed requests are fully parsed and validated before the tenant check fires. The authorization middleware would have rejected them earlier with less attack surface and less wasted work.
2. **Two opt-out mechanisms that must stay in sync.** Consumers that also wire HTTP-side enforcement carry `[AllowMissingTenant]` on the request type *and* an `.AllowAnonymous()` / no-tenant policy on the endpoint. The two are easy to drift.
3. **Duplicate work without independent value.** The Mediator behavior and the HTTP gate check the same invariant. This is structurally different from the read/write defense the EF query filter and write guard provide together — those guard distinct concerns, the auth-and-Mediator pair does not.

The right shape for HTTP-dispatched commands is a single `IAuthorizationRequirement` in `FallbackPolicy` / `DefaultPolicy`, composable with role and permission requirements, enforced by `UseAuthorization` before model binding. Non-HTTP execution paths (messaging consumers via the existing tenant-propagation filter, background jobs via explicit `currentTenant.Change(...)` scopes) do not need the Mediator behavior to catch their misuse — the data-layer guards (`MultiTenantFilter`, write guard) are the load-bearing defense there.

---

## Requirements

**Authorization primitive**
- R1. Add a `TenantRequirement` implementing `IAuthorizationRequirement` and a paired `AuthorizationHandler<TenantRequirement>` that resolves the current tenant from `ICurrentTenant` and decides success or failure.
- R2. The handler succeeds when `ICurrentTenant.Id` is non-empty.
- R3. The handler succeeds when the active endpoint has `[AllowMissingTenant]` metadata, regardless of `ICurrentTenant.Id`.
- R4. When the handler fails, it attaches a structured failure reason that downstream auth-result handling can translate into a `g:tenant-required` ProblemDetails response — not a generic forbidden response.

**Endpoint opt-out**
- R5. Provide a `[AllowMissingTenant]` attribute usable as endpoint metadata (controllers, action methods, Minimal-API endpoints). This is a new attribute living in `Headless.Api.Core`; it is not the same type as the deleted Mediator-side attribute.
- R6. Provide a fluent `.AllowMissingTenant()` extension on `IEndpointConventionBuilder` that attaches the attribute as endpoint metadata.

**Removal of Mediator-side tenant gating**
- R7. Delete `TenantRequiredBehavior<TRequest, TResponse>`.
- R8. Delete the Mediator-targeting `AllowMissingTenantAttribute` (the type targeting `Class | Struct` on request types).
- R9. Delete the `.Mediator(m => m.RequireTenant())` registration hook, the `RequireTenant()` method, and the now-empty `HeadlessMediatorTenancyBuilder`. The `.Mediator(...)` extension on `HeadlessTenancyBuilder` is removed as well.
- R10. Delete supporting wiring (`AddMediatorTenantRequiredBehavior`, related tests, related setup tests) that exists only to serve the Mediator gate.

**Exception mapping**
- R11. `HeadlessApiExceptionHandler` remaps `MissingTenantContextException` from 400 to 403, keeping the existing `TenantContextRequired` error code so both the auth-gate path and any leftover server-side throw (EF write guard, messaging publish guard) return the same `g:tenant-required` shape.

**Documentation**
- R12. Update `docs/llms/multi-tenancy.md` to describe the canonical wiring as `AddAuthorization` with a policy that includes `TenantRequirement`, plus messaging propagation and EF write guard. Remove the Mediator-behavior section. Update related package READMEs (`Headless.Api.Core/README.md`, `Headless.Mediator/README.md`, `Headless.MultiTenancy/README.md`) where they describe tenant posture seams.

**Posture manifest**
- R13. Record the new HTTP-side enforcement in `TenantPostureManifest` under an authorization seam (replacing the Mediator seam record) so `HeadlessTenancyStartupValidator` and consumers reading posture see the new shape.

---

## Acceptance Examples

- AE1. **Covers R1, R2.** Given an HTTP request with a valid tenant claim and a policy that includes `TenantRequirement`, when the endpoint executes, the request reaches the handler with `ICurrentTenant.Id` populated.
- AE2. **Covers R1, R4.** Given an HTTP request from an authenticated user with no tenant claim and a policy that requires `TenantRequirement`, when authorization runs, the response is 403 with ProblemDetails carrying the `TenantContextRequired` error code (`g:tenant-required`).
- AE3. **Covers R3, R5, R6.** Given an endpoint marked `.AllowMissingTenant()` (or `[AllowMissingTenant]`) under the same policy, when an authenticated user with no tenant claim calls it, the request succeeds and reaches the handler.
- AE4. **Covers R11.** Given a request that passes authorization (e.g., a misconfigured endpoint without the tenant requirement) but reaches code that throws `MissingTenantContextException` (EF write guard or messaging publish guard), when the exception bubbles, `HeadlessApiExceptionHandler` returns 403 with the `TenantContextRequired` ProblemDetails code.

---

## Success Criteria

- A consumer can enforce tenant presence pre-model-bind by registering a single policy with `TenantRequirement`, without touching the Mediator pipeline.
- An auditor reading the framework can find one tenant-required gate per execution boundary: authorization for HTTP, propagation filter for messaging, write guard for EF saves. No duplicated check at the Mediator boundary.
- Both 403 responses (auth-gate failure) and 403 responses (server-side guard throwing `MissingTenantContextException`) carry the same structured `TenantContextRequired` ProblemDetails code, so client error handling is uniform.
- Downstream agents executing `dev-plan` can locate the new types, the deletions, the exception remapping, and the docs to update without re-deriving product behavior.

---

## Scope Boundaries

- No deprecation cycle for the deleted Mediator types — framework is greenfield with no deployed consumers; `[Obsolete]` would be ceremony with no audience.
- No `RequireAuthBehavior<,>` / `[RequireAuth]` Mediator equivalent. `RequireAuthorization(...)` on endpoints plus `IAuthorizationService` for resource-based checks from handlers remain canonical.
- No changes to `MultiTenantFilter`, the EF tenant write guard, or `CrossTenantWriteException`. These guard distinct invariants and stay.
- No changes to `TenantPropagationConsumeFilter` for messaging or to background-job tenant scoping. A `TenantScopedJob<T>` abstraction is interesting future work and explicitly out of scope here.
- No new composite policies (role + tenant, permission + tenant). Consumers compose those with `AuthorizationPolicyBuilder` themselves.

---

## Key Decisions

- **Authorization middleware over Mediator pipeline.** Pre-model-bind enforcement, native composition with role and permission requirements, smaller attack surface, single opt-out surface.
- **`Headless.Api.Core` as the home package.** Already framework-references `Microsoft.AspNetCore.App` and owns `SetupApiTenancy`. Adding ASP.NET Core authorization types to `Headless.MultiTenancy` would break that package's invariant of being HTTP-framework-agnostic. Mirrors the existing precedent of `PermissionRequirement` living in `Headless.Permissions.Core`.
- **Straight removal of Mediator pieces, including the builder shell.** The greenfield rule defaults to removal over deprecation; removing the now-empty `HeadlessMediatorTenancyBuilder` and the `.Mediator(...)` hook keeps the tenancy builder surface honest (one fewer dangling extension point).
- **Unify on 403 + `TenantContextRequired` for both paths.** The auth handler attaches a structured failure reason so ProblemDetails carries the specific code, and `HeadlessApiExceptionHandler` remaps the leftover exception throw from 400 to 403. Consumers see one error shape across both code paths.
- **Reuse the existing `TenantContextRequired` / `g:tenant-required` problem code.** No new error code, no doc churn for the code itself — only the HTTP status and the gate location change.

---

## Dependencies / Assumptions

- The framework already exposes a primitive for translating an `AuthorizationFailureReason` into a ProblemDetails response (custom `IAuthorizationMiddlewareResultHandler` or equivalent). If no such primitive exists today, planning adds one as part of R4. **Unverified assumption — confirm during planning.**
- `HeadlessApiExceptionHandler` is reachable from authorization-middleware failures via the existing problem-details pipeline; the auth handler does not need its own response-writing path.
- `ICurrentTenant` is resolved before authorization runs in the configured pipeline. `UseHeadlessTenancy()` ordering vs `UseAuthorization()` may need to be documented or asserted in startup validation — flagged for planning.
- No consuming repository inside this framework depends on the Mediator behavior or its attribute. (Verified: only the package itself and its own tests reference these symbols.)

---

## Outstanding Questions

### Deferred to Planning

- [Affects R4][Technical] Which exact ASP.NET primitive carries the structured failure reason from the handler to `HeadlessApiExceptionHandler` — a custom `IAuthorizationMiddlewareResultHandler`, an `IAuthorizationFailureHandler`, or a typed exception thrown from the handler that the existing exception handler already catches?
- [Affects R5, R7][Technical] Should the new endpoint-side `AllowMissingTenantAttribute` live in the same namespace as the old one (`Headless.Mediator`) or in `Headless.Api.Core` / a tenancy sub-namespace? The user picked the package; namespace placement is a planning-time call.
- [Affects R13][Technical] What seam name and capability labels best describe the new authorization-middleware enforcement in `TenantPostureManifest`? (`"Authorization"`, `"require-tenant"` is a candidate.)
- [Affects R12][Needs research] Identify every README and LLM-doc reference to `.Mediator(m => m.RequireTenant())`, `TenantRequiredBehavior`, or the Mediator `AllowMissingTenantAttribute` so the doc sweep is complete.
