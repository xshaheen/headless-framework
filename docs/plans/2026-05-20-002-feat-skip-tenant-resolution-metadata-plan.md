---
title: "feat(api): add SkipTenantResolution endpoint metadata"
type: feat
status: active
depth: standard
created: 2026-05-20
issue: 251
---

# feat(api): add SkipTenantResolution endpoint metadata

**Origin:** GitHub issue [#251](https://github.com/xshaheen/headless-framework/issues/251). Spec adjusted during planning — see Key Technical Decisions for the deltas from the issue body.

## Summary

Add an endpoint-level opt-out from HTTP claims-based tenant resolution. Endpoints (or route groups, or MVC controllers/actions) marked with `[SkipTenantResolution]` / `.SkipTenantResolution()` will not have `ICurrentTenant` mutated by `TenantResolutionMiddleware`, even when the authenticated principal carries a tenant claim. Mediator, EF write-guards, and messaging tenant requirements stay enforced — this is an HTTP-layer marker only.

The opt-out mirrors the existing `[AllowMissingTenant]` / `.AllowMissingTenant()` shape exactly: bare sealed attribute that doubles as endpoint metadata, member of the existing `EndpointConventionBuilderExtensions` extension block, attribute targets `Class | Method` so it works on Minimal API, route groups, and MVC controllers/actions out of the box.

---

## Problem Frame

`TenantResolutionMiddleware` (`src/Headless.Api.Core/Middlewares/TenantResolutionMiddleware.cs`) currently mutates `ICurrentTenant` for **every authenticated request whose principal carries the configured tenant claim**. This is the right default for tenant-scoped users but breaks two real scenarios:

1. **Host / cross-tenant / admin endpoints reached by claim-carrying principals.** Documented framework guidance (`docs/llms/multi-tenancy.md` line 68) is "do not mint the tenant claim on admin / host / service-account principals." That guidance is enforced by convention, not by the framework. When a platform-admin principal who also belongs to a tenant (legitimately, as a user) hits `GET /admin/tenants`, the middleware sets `ICurrentTenant` from that user-side claim, and any downstream EF global filter on tenant silently scopes the cross-tenant query to one row. Silent wrong-answer, not an exception.

2. **Endpoints whose tenant comes from a non-claim source.** Subdomain (`acme.example.com`), path segment (`/t/{tenantId}/...`), or webhook signature verification. The claim-based middleware fights the alternate resolver — whichever runs last wins, and the result depends on registration order rather than the endpoint's stated intent.

Today the only mitigations are pipeline-level branching (brittle, invisible at the endpoint definition site) or carefully scrubbing the tenant claim from admin principals (orthogonal concern, not the framework's job). The opt-out marker makes intent first-class at the endpoint.

**What the issue lists but is *not* the real motivation:** health probes, OpenAPI, public unauthenticated endpoints. The current middleware is already a no-op pass-through for unauthenticated requests and for authenticated requests without a tenant claim. Those endpoints do not need the marker for correctness — see Scope Boundaries.

---

## Scope Boundaries

**In scope:**

- New `SkipTenantResolutionAttribute` in `src/Headless.Api.Core/MultiTenancy/`.
- New `.SkipTenantResolution()` extension on `IEndpointConventionBuilder`, added to the existing `EndpointConventionBuilderExtensions` block (same folder).
- `TenantResolutionMiddleware` honors the new metadata — bypasses claim resolution and the misordering warning, still sets the `HeadlessTenancyResolutionApplied` feature flag so downstream exception handling stays consistent.
- Integration test coverage for the four endpoint topologies (Minimal API, route group, MVC action, MVC class-level) plus the composition with `[AllowMissingTenant]`.
- Doc updates to `src/Headless.Api.Core/README.md` and `docs/llms/multi-tenancy.md` distinguishing the new *resolution* opt-out from the existing *authorization* opt-out.

**Out of scope:**

- Mediator tenant guards, EF write guards, messaging tenant requirements. The marker is HTTP-layer; downstream guards keep enforcing tenant context. Explicit non-goal in issue #251.
- Tenant-resolution-skip vocabulary in `Headless.MultiTenancy` (the abstraction-only package). The metadata lives where the consumer lives — `Headless.Api.Core`.
- Per-request runtime conditional skip (e.g., "skip when header X is present"). The marker is endpoint-static metadata; runtime conditionals belong in custom middleware.
- Host-mode global skip (a startup flag that disables claim resolution for the whole app). YAGNI; the existing path of "don't call `tenancy.Http(...)`" already covers that.

### Deferred to Follow-Up Work

- **Opt-back-in inverse (`[RequireTenantResolution]` / `.RequireTenantResolution()`).** Would let a route group declare `.SkipTenantResolution()` and a single child opt back in. The "last attribute wins" reverse-iteration pattern in `TenantRequirementHandler._AllowsMissingTenant` already proves the framework can support this trivially. No real consumer yet; defer until one shows up.
- **MVC controller-style integration tests with their own fixture.** The attribute targets `Class | Method` so it works on controllers/actions for free, but dedicated MVC integration tests (mirroring `PublicTenantRequirementController` in `TenantRequirementTests.cs`) are deferred to a follow-up if MVC adoption justifies the surface. The attribute itself remains functional on controllers.
- **`docs/solutions/` entry on claim-based tenant resolution as a privilege-escalation surface.** Learnings-research flagged that no `docs/solutions/` entry covers middleware-level tenant resolution despite multiple footguns documented in `docs/llms/multi-tenancy.md`. Worth a `/dev-compound` entry post-ship.

---

## Requirements Traceability

| Origin requirement (issue #251) | Plan disposition |
|---|---|
| Add endpoint metadata that tells the middleware to skip resolution | Honored — `SkipTenantResolutionAttribute` (U1), middleware check (U2) |
| Minimal API endpoint can call `.SkipTenantResolution()` | Honored — extension member, tested in U3 |
| Route groups can call `.SkipTenantResolution()` and metadata applies to children | Honored — same extension covers `IEndpointConventionBuilder` (groups implement it), tested in U3 |
| MVC controllers/actions can use `[SkipTenantResolution]` | Honored at attribute level via `AttributeTargets.Class | Method`; dedicated MVC integration tests deferred (see Deferred) |
| Middleware skips resolution when metadata present | Honored — U2 |
| Skipped endpoints do not mutate `ICurrentTenant` even with tenant claim | Honored — U2; tested in U3 |
| Skipped endpoints do not emit misordering warning | Honored — bypass `_WarnIfMiddlewareLikelyMisordered` for marked endpoints (U2) |
| Non-skipped endpoints retain current behavior | Honored — regression coverage in U3 |
| `UseHeadlessTenancy()` startup validation unchanged; remains idempotent | Honored — no changes to `SetupApiTenancy` / `HeadlessTenancyBuilder` |
| Integration tests cover Minimal API skip, route group skip, MVC attribute skip, normal resolution | Honored at attribute/extension level in U3; MVC-specific harness deferred |
| README + multi-tenancy.md updated | Honored — U4, target paths corrected (issue's `src/Headless.Api/README.md` does not exist) |
| Marker must not bypass Mediator / EF / messaging tenant guards | Honored — explicit non-goal carried forward to Scope Boundaries |
| `ISkipTenantResolutionMetadata` marker interface | **Rejected** — see Key Technical Decisions #1. The existing `[AllowMissingTenant]` / `[RequireTenant]` pattern uses the attribute *as* the metadata; introducing a marker interface here would diverge from the established convention without a concrete consumer to justify the indirection. |

---

## Key Technical Decisions

### 1. Attribute-as-metadata, no marker interface

**Decision:** Drop the issue's `ISkipTenantResolutionMetadata` marker interface. The attribute IS the metadata, consumed via `endpoint.Metadata.GetMetadata<SkipTenantResolutionAttribute>()`.

**Rationale:** `AllowMissingTenantAttribute` and `RequireTenantAttribute` already follow this shape exactly. Introducing a marker interface here adds an abstraction layer that no consumer needs (no scenario where a non-attribute type would implement the marker). The cost is consistency with the sibling tenancy markers; the only gained flexibility is "consumers could attach arbitrary metadata implementing the marker," which has no requested use case. Greenfield framework — favor the simpler shape.

**Reversibility:** Trivial. If a marker-interface consumer appears later, add the interface, have the attribute implement it, change the middleware check from `GetMetadata<SkipTenantResolutionAttribute>()` to `GetMetadata<ISkipTenantResolutionMetadata>()`. No breaking change.

### 2. Naming: `SkipTenantResolution` matches the issue, distinguishes from `AllowMissingTenant`

**Decision:** Type name `SkipTenantResolutionAttribute`, extension method `.SkipTenantResolution()`.

**Rationale:** The existing tenancy verbs are `AllowMissingTenant` (authorization opt-out — "this endpoint doesn't require a tenant in scope") and `RequireTenant` (opt-back-in). The new attribute is the **resolution** opt-out — "this endpoint doesn't run the claim-based resolver." The two are conceptually distinct (`docs/llms/multi-tenancy.md` already explicitly differentiates `[AllowMissingTenant]` from `[AllowAnonymous]`; the new attribute is a third axis). `Skip` is more precise than `Disable` or `Without` for this semantics: the middleware skips a step rather than turning a feature off.

### 3. Middleware bypass set, including misordering warning

**Decision:** When the marker is present, the middleware sets `HeadlessTenancyResolutionApplied` (the feature flag consumed by `HeadlessApiExceptionHandler`), calls `next`, and returns. No claim read, no `ICurrentTenant.Change`, no `_WarnIfMiddlewareLikelyMisordered` call.

**Rationale:**
- Setting the feature flag preserves the "middleware ran" invariant from the exception-handler perspective. If a handler on a skipped endpoint accidentally reads `ICurrentTenant.Id` and throws `MissingTenantContextException`, the exception handler should NOT emit "you forgot to wire up `UseHeadlessTenancy()`" — the middleware did run; the user explicitly chose to skip. The misleading-warning surface is what the feature flag is for; setting it on the skip path keeps that semantic correct.
- Bypassing `_WarnIfMiddlewareLikelyMisordered` is the right call because the warning is about middleware ordering relative to `UseAuthentication`. A skipped endpoint doesn't care whether claims were ever parsed; emitting the warning would be misleading.

### 4. Reverse-iteration "last attribute wins" semantics

**Decision:** Use `endpoint.Metadata.GetMetadata<SkipTenantResolutionAttribute>()` (returns the *last* match in metadata order). This naturally gives "action overrides controller, member overrides group" behavior consistent with the existing `[AllowMissingTenant]` convention.

**Rationale:** Matches `TenantRequirementHandler._AllowsMissingTenant`'s reverse-iteration discipline and the test invariant `should_require_tenant_when_minimal_api_endpoint_overrides_group_allow_missing`. No opt-back-in inverse (`[RequireTenantResolution]`) is shipped (see Deferred), so a richer reverse-iteration switch is not needed — single-attribute lookup is sufficient and simpler.

### 5. File layout — extend, do not create

**Decision:**
- New file: `src/Headless.Api.Core/MultiTenancy/SkipTenantResolutionAttribute.cs` (mirrors `AllowMissingTenantAttribute.cs` byte-for-byte in shape).
- Modify: `src/Headless.Api.Core/MultiTenancy/EndpointConventionBuilderExtensions.cs` to add the `.SkipTenantResolution()` member alongside `AllowMissingTenant` / `RequireTenant`.
- Modify: `src/Headless.Api.Core/Middlewares/TenantResolutionMiddleware.cs` for the metadata check.

**Rationale:** AUTHORING.md "append, don't rewrite" rule; sibling-pattern consistency.

---

## High-Level Technical Design

Directional pseudo-shape of the updated `TenantResolutionMiddleware.InvokeAsync` decision flow. **This is review guidance, not implementation specification** — the implementer should treat shape and ordering as fixed but invent concrete syntax.

```text
InvokeAsync(context, currentTenant):
    context.Features.Set(HeadlessTenancyResolutionApplied.Instance)

    endpoint = context.GetEndpoint()
    skipMarker = endpoint?.Metadata.GetMetadata<SkipTenantResolutionAttribute>()
    if skipMarker is not null:
        await next(context)                 # skip claim read, skip warning
        return

    if not authenticated:
        _WarnIfMiddlewareLikelyMisordered(context)
        await next(context)
        return

    tenantId = _GetTenantId(user)
    if tenantId is null/whitespace:
        await next(context)
        return

    using scope = currentTenant.Change(tenantId)
    await next(context)
```

Decision-matrix the integration tests should anchor on:

| Endpoint marked | Principal | Tenant claim | Expected `ICurrentTenant.Id` | Misordering warning fires |
|---|---|---|---|---|
| `[SkipTenantResolution]` | unauthenticated | — | null | no |
| `[SkipTenantResolution]` | authenticated | absent | null | no |
| `[SkipTenantResolution]` | authenticated | present | **null (key behavior)** | no |
| unmarked | authenticated | present | claim value | no |
| unmarked | unauthenticated | — | null | conditional (existing logic) |

---

## Implementation Units

### U1. SkipTenantResolutionAttribute and endpoint extension

**Goal:** Introduce the public API surface — the attribute and the `.SkipTenantResolution()` extension member. Mirror `AllowMissingTenantAttribute` byte-for-byte in shape.

**Requirements:** Issue #251 — endpoint metadata + Minimal API extension + route group extension + MVC attribute targets.

**Dependencies:** none.

**Files:**

- `src/Headless.Api.Core/MultiTenancy/SkipTenantResolutionAttribute.cs` (new)
- `src/Headless.Api.Core/MultiTenancy/EndpointConventionBuilderExtensions.cs` (modify — add member)

**Approach:**

- `SkipTenantResolutionAttribute` is `[PublicAPI] [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)] public sealed class ... : Attribute;` — body-less primary syntax, same as `AllowMissingTenantAttribute`.
- Add `.SkipTenantResolution()` extension member to the existing `EndpointConventionBuilderExtensions` block inside the `extension<TBuilder>(TBuilder builder) where TBuilder : IEndpointConventionBuilder` declaration. Delegates to `builder.WithMetadata(new SkipTenantResolutionAttribute())`. No `With…` prefix on the method name (matches `AllowMissingTenant` / `RequireTenant` convention; `WithIdempotency` carries config, this does not).
- File-level `#pragma warning disable IDE0130` + namespace shim to `Microsoft.AspNetCore.Builder` is already in place — do not re-introduce or move.

**Patterns to follow:**

- `src/Headless.Api.Core/MultiTenancy/AllowMissingTenantAttribute.cs` for the attribute shape.
- `src/Headless.Api.Core/MultiTenancy/EndpointConventionBuilderExtensions.cs` for the extension member shape (C# 14 extension members, not classic extension methods).

**Test suite design:** No standalone tests — the attribute's only observable behavior is via the middleware. Verification happens in U3 via integration tests that mark endpoints and assert `ICurrentTenant.Id`.

**Test scenarios:** `Test expectation: none — pure API-surface declaration with no behavior; observable effect is covered by U3 integration tests against the middleware.`

**Verification:**

- `make build` succeeds with no new warnings.
- `make format-check` passes.
- The new file starts with the copyright header.
- IntelliSense surfaces `.SkipTenantResolution()` on `RouteHandlerBuilder` and `RouteGroupBuilder` (both implement `IEndpointConventionBuilder`).

---

### U2. TenantResolutionMiddleware honors the metadata

**Goal:** Make `TenantResolutionMiddleware` short-circuit when the resolved endpoint carries `SkipTenantResolutionAttribute`.

**Requirements:** Issue #251 — middleware skips resolution; no `ICurrentTenant` mutation; no misordering warning; idempotency of `UseHeadlessTenancy()` preserved.

**Dependencies:** U1.

**Files:**

- `src/Headless.Api.Core/Middlewares/TenantResolutionMiddleware.cs` (modify)

**Approach:**

- Add metadata lookup immediately after setting `HeadlessTenancyResolutionApplied` (so the feature flag is still recorded — see Key Technical Decision #3).
- Use `context.GetEndpoint()?.Metadata.GetMetadata<SkipTenantResolutionAttribute>()`. If non-null, `await next(context)` and return.
- Crucially, this short-circuit precedes the `if (context.User.Identity?.IsAuthenticated != true)` check — skipping should suppress both the claim read AND the misordering warning.
- No new options, no new public API on the middleware itself, no new log message (a future "skipped due to endpoint metadata" debug log is YAGNI — defer if ops asks for it).
- The middleware's existing `Argument.IsNotNull` discipline stays.

**Patterns to follow:**

- `TenantRequirementHandler._AllowsMissingTenant` in the same folder for the metadata-lookup idiom (though it uses reverse iteration; here single-attribute `GetMetadata<T>` is enough — see Key Technical Decision #4).

**Test suite design:** Covered by U3 integration tests. No unit-test layer added — existing `TenantResolutionMiddleware` coverage is integration-only and the dependency on `HttpContext`/`Endpoint`/`ClaimsPrincipal` makes integration the natural seam.

**Test scenarios:** Covered in U3. No new scenarios localized to this unit; the middleware change is observed end-to-end.

**Verification:**

- `make build` succeeds, no new warnings.
- All U3 scenarios pass.
- Existing `TenantResolutionMiddlewareTests` continue to pass — regression check for non-skipped endpoints.

---

### U3. Integration tests for the four endpoint topologies

**Goal:** Cover the decision matrix from High-Level Technical Design with integration tests that drive a real `WebApplication` and assert observable `ICurrentTenant` state via a captured endpoint response.

**Requirements:** Issue #251 acceptance criteria — Minimal API skip, route group skip, MVC attribute skip (at attribute level — full MVC harness deferred per Scope), normal non-skipped resolution stays correct.

**Dependencies:** U1, U2.

**Files:**

- `tests/Headless.Api.Tests.Integration/TenantResolutionMiddlewareTests.cs` (modify — extend `_CreateAppAsync` and add scenarios) OR
- `tests/Headless.Api.Tests.Integration/SkipTenantResolutionMiddlewareTests.cs` (new — preferred if the additions exceed ~5 scenarios; mirror the existing file's harness verbatim).

Implementer chooses one; the test harness is identical either way. The plan's recommendation: new file if more than three scenarios are added (current file is already ~430 lines).

**Approach:**

- Reuse the in-process `WebApplication.CreateBuilder` + ephemeral-port + `HttpClient` harness from `TenantResolutionMiddlewareTests` and `TenantRequirementTests`.
- Reuse the `TestAuthenticationHandler` pattern (reads `X-Test-User`, `X-Test-Tenant`, `X-Test-Unauthenticated` headers to materialize a `ClaimsPrincipal`).
- Reuse the `TenantResponse` capture endpoint pattern: `app.MapGet("/probe", (ICurrentTenant t) => Results.Json(new TenantResponse(t.Id, t.IsAvailable)))` — mark the probe variants with `.SkipTenantResolution()` / `[SkipTenantResolution]`.
- Reuse `CapturingLoggerProvider` to assert that the misordering warning is NOT emitted on skip-marked endpoints.
- For MVC at the attribute level (no full controller harness shipped — see Deferred), one test that marks the controller via `[ApiController]` + `[SkipTenantResolution]` + ` [Route("/mvc-skip")]` and asserts the same `ICurrentTenant.Id == null` behavior.

**Patterns to follow:**

- `tests/Headless.Api.Tests.Integration/TenantResolutionMiddlewareTests.cs` for the auth + endpoint-probe + log-capture pattern.
- `tests/Headless.Api.Tests.Integration/TenantRequirementTests.cs` lines 265–274 for the route-group + member-override pattern.
- `tests/Headless.Api.Tests.Integration/TenantRequirementTests.cs` lines 383–412 for the bottom-of-file controller declaration convention.

**Test suite design:** All scenarios are integration. They live in `Headless.Api.Tests.Integration`. No new fixture is needed; the existing per-test `WebApplication.CreateBuilder` pattern is sufficient. Reuse `TestAuthenticationHandler`, `CapturingLoggerProvider`, and `_AddDefaultHeadlessSecurityConfiguration` helpers verbatim — extract to a shared partial if duplicated, otherwise keep inline.

**Test scenarios:**

*Skip behavior (claim-carrying principal):*

1. `should_not_mutate_current_tenant_when_endpoint_marked_skip_resolution_and_claim_present` — Minimal API endpoint with `.SkipTenantResolution()`, authenticated principal with valid tenant claim → `ICurrentTenant.Id == null`, `IsAvailable == false`. **(Primary correctness scenario.)**
2. `should_not_mutate_current_tenant_when_route_group_marked_skip_resolution_and_claim_present` — `app.MapGroup("/grouped").SkipTenantResolution()`, child Minimal API endpoint → same as #1.
3. `should_not_mutate_current_tenant_when_mvc_controller_marked_skip_resolution_and_claim_present` — Controller class with `[SkipTenantResolution]`, action with route `/mvc-skip` → same as #1.
4. `should_not_mutate_current_tenant_when_mvc_action_marked_skip_resolution_and_claim_present` — Plain controller, action method decorated with `[SkipTenantResolution]` → same as #1.

*Skip behavior (no claim / no principal):*

5. `should_not_mutate_current_tenant_when_endpoint_marked_skip_resolution_and_unauthenticated` — Same endpoint as #1, no principal → `ICurrentTenant.Id == null`. Regression: confirms skip path doesn't accidentally read claims.
6. `should_not_emit_middleware_ordering_warning_when_endpoint_marked_skip_resolution_and_unauthenticated` — Use `CapturingLoggerProvider`; assert no entry with EventId name `HEADLESS_TENANCY_MIDDLEWARE_ORDERING` is captured for a skip-marked endpoint reached pre-`UseAuthentication`.

*Non-skip regression (must stay correct):*

7. `should_mutate_current_tenant_for_unmarked_endpoint_when_claim_present` — Existing behavior preserved; an unmarked endpoint adjacent to a marked one still resolves tenant from claim.
8. `should_mutate_current_tenant_for_unmarked_endpoint_in_route_group_that_does_not_skip` — Group without `.SkipTenantResolution()`, child endpoint → tenant claim resolved as today.

*Composition with sibling markers:*

9. `should_compose_skip_resolution_with_allow_missing_tenant_independently` — Endpoint marked with both `.SkipTenantResolution()` and `.AllowMissingTenant()`, authenticated principal without tenant claim → `ICurrentTenant.Id == null` AND request not rejected by authorization (proves they're independent opt-outs solving different problems; the docs explicitly call out this distinction).

*Feature-flag invariant:*

10. `should_set_tenancy_resolution_applied_feature_when_endpoint_marked_skip_resolution` — Inspect `HttpContext.Features.Get<HeadlessTenancyResolutionApplied>()` from a probe endpoint marked skip; expect non-null. Confirms the exception-handler invariant from Key Technical Decision #3. (`HeadlessTenancyResolutionApplied` is currently `internal sealed`; if `[InternalsVisibleTo("Headless.Api.Tests.Integration")]` is already declared, assert directly; otherwise, assert the consequent — that `MissingTenantContextException` thrown in a skip-marked handler does NOT log the "middleware-not-applied" warning. Implementer to pick the lower-friction shape.)

**Verification:**

- All 10 scenarios above implemented and passing.
- `make test-project TEST_PROJECT=tests/Headless.Api.Tests.Integration` runs cleanly.
- No new failures in `TenantResolutionMiddlewareTests` or `TenantRequirementTests`.
- Line coverage for `src/Headless.Api.Core/Middlewares/TenantResolutionMiddleware.cs` stays at or above the project minimum (≥85% line, ≥80% branch per `CLAUDE.md`).

---

### U4. Documentation — distinguish resolution opt-out from authorization opt-out

**Goal:** Add the new opt-out to both consumer-facing surfaces, taking care to differentiate it from `[AllowMissingTenant]` (authorization-pipeline opt-out) and `[AllowAnonymous]` (auth opt-out). All three are independent.

**Requirements:** Issue #251 — README + multi-tenancy.md updated. AUTHORING.md "same-commit drift" rule for API-surface changes.

**Dependencies:** U1, U2 (so the docs match what shipped).

**Files:**

- `src/Headless.Api.Core/README.md` (modify) — issue body's path `src/Headless.Api/README.md` does not exist; this is the correct target.
- `docs/llms/multi-tenancy.md` (modify)

**Approach:**

- **`src/Headless.Api.Core/README.md`:**
  - `Key Features` bullet list — add a one-line bullet for `[SkipTenantResolution]` / `.SkipTenantResolution()` alongside the existing `[AllowMissingTenant]` bullet.
  - `Building Blocks Quick Reference` (or the equivalent Quick Start section in the existing file shape) — add a usage example. Match the file's existing section order; do not restructure.
  - One sentence distinguishing the new opt-out from `[AllowMissingTenant]`: "`[SkipTenantResolution]` prevents the resolution middleware from mutating `ICurrentTenant`; `[AllowMissingTenant]` prevents the authorization pipeline from rejecting requests without a tenant. They solve different problems and are independently composable."

- **`docs/llms/multi-tenancy.md`:**
  - Lines around 117–119 (the `UseHeadlessTenancy()` runtime description) — add a bullet: middleware now also consults `[SkipTenantResolution]` endpoint metadata before mutating `ICurrentTenant`.
  - `## HTTP Authorization Requirement` section (lines around 168–219) — add a sibling subsection `## HTTP Resolution Opt-Out` that mirrors the existing prose template ("Mark intentional X with `[Attribute]` or `.Method()`."), names the two real use cases from Problem Frame (claim-carrying admin principals, alternate resolvers), and includes the explicit "different from `[AllowMissingTenant]`" distinction.
  - Lines around 234 (the three-tenant-states paragraph: never set / explicit host / tenant-scoped) — one sentence noting `[SkipTenantResolution]` as a fourth case in the same vocabulary ("middleware ran but explicitly opted out").
  - `Agent Instructions` section (lines around 65–77 per AUTHORING.md) — add one instruction line about choosing `[SkipTenantResolution]` for cross-tenant / admin / alternate-resolver endpoints. This is the highest-leverage doc edit per the research findings.

- Tone and naming: follow the file's existing template ("Mark intentional host-level, public, system, or console-bootstrap endpoints with `[AllowMissingTenant]` or `.AllowMissingTenant()`."). No emojis, no marketing adjectives, no version numbers, no dates in headings. AUTHORING.md "append, don't rewrite" — do not restructure README sections; integrate inside the existing H2s.

- Both files updated in the **same commit** per AUTHORING.md "Public API surface change triggers both docs in same commit; files must not disagree on facts."

**Patterns to follow:**

- `docs/authoring/AUTHORING.md` for the drift checklist.
- Existing `[AllowMissingTenant]` prose in both files as the template for the new sections.

**Test suite design:** No automated tests. Doc accuracy is verified by review against U1–U3 surfaces.

**Test scenarios:** `Test expectation: none — documentation update; correctness verified by review against the shipped API surface from U1–U3.`

**Verification:**

- Both files reference the new attribute and extension.
- The distinction between resolution opt-out (`[SkipTenantResolution]`) and authorization opt-out (`[AllowMissingTenant]`) is stated explicitly in both files.
- The example snippets compile mentally against the shipped API (attribute targets, extension signature).
- AUTHORING.md drift checklist passes: same-commit update, no emojis/dates/version-numbers in headings, install commands unchanged, banned hedging/marketing prose absent.

---

## System-Wide Impact

| Surface | Impact |
|---|---|
| `Headless.Api.Core` public API | One new attribute, one new extension member. Additive — no breaking changes. |
| `TenantResolutionMiddleware` runtime | One new branch at the top of `InvokeAsync`. Hot-path cost: a single `Endpoint.Metadata.GetMetadata<T>` lookup per request (cheap, ~constant-time, ASP.NET Core does this in many places). |
| `HeadlessApiExceptionHandler` | No code change. The `HeadlessTenancyResolutionApplied` feature flag is still set on skipped requests, so the "middleware-not-applied" surface stays consistent. |
| `HeadlessTenancyBuilder` / `SetupApiTenancy` / startup validation | No change. Issue's "remains idempotent and startup validation still fails when HTTP tenancy is configured but the middleware is not applied" is preserved. |
| Downstream tenant guards (`Headless.Mediator.*`, `Headless.Orm.EntityFramework`, `Headless.Messaging.Core`) | No change. The marker is HTTP-only. Any handler that reads `ICurrentTenant.Id` on a skip-marked endpoint will still raise `MissingTenantContextException` — that's the intended UX. The marker says "don't auto-resolve," not "downstream guards stop working." |
| Consumers of `[AllowMissingTenant]` / `[RequireTenant]` | No change. The two markers compose orthogonally with the new one. Coverage scenario #9 in U3 anchors this. |

---

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Implementer mirrors the marker-interface design from the issue body, ignoring Key Technical Decision #1 | Medium | Decision #1 is the first KTD and explicitly calls out the deviation from the issue. The Requirements Traceability row also flags the rejection. |
| Skip metadata at a route-group level doesn't propagate to children because of how ASP.NET Core's endpoint metadata resolves | Low | The existing `[AllowMissingTenant]` route-group test (`TenantRequirementTests` lines 265–274 + assertions) already proves the convention works for this metadata shape. The new attribute mirrors it byte-for-byte. |
| `HeadlessTenancyResolutionApplied` flag interaction with the exception handler regresses | Low | Decision #3 documents the intent; scenario #10 anchors the invariant in tests. |
| Doc drift between `src/Headless.Api.Core/README.md` and `docs/llms/multi-tenancy.md` | Medium | AUTHORING.md mandates same-commit updates. U4 includes a doc-drift verification step. |
| MVC attribute targets work in principle but no MVC-specific integration test catches a subtle controller-metadata bug | Low-Medium | Scenarios #3 and #4 cover MVC at the attribute level using the existing inline-controller pattern; dedicated MVC harness deferred (see Scope) on the assumption that the controller surface in this framework is thin. If MVC adoption grows, revisit. |
| Future demand for `[RequireTenantResolution]` opt-back-in surfaces after ship, requiring a richer reverse-iteration semantics | Low | Deferred follow-up explicitly recognizes this. The change is backwards-compatible — add the inverse attribute and switch `GetMetadata<T>()` to a reverse-iteration switch (the same pattern `TenantRequirementHandler._AllowsMissingTenant` uses). No public-API rework. |

---

## Deferred Implementation Notes

- **Exact integration-test file choice (extend existing vs. new file)** — decide at implementation time based on how much the U3 scenarios bloat `TenantResolutionMiddlewareTests.cs`. Plan recommendation: new file if >3 scenarios added; otherwise extend.
- **`HeadlessTenancyResolutionApplied` visibility** — currently `internal sealed`. If scenario #10 needs to assert on it directly, implementer either (a) adds `[assembly: InternalsVisibleTo("Headless.Api.Tests.Integration")]` to `Headless.Api.Core` if not already present, or (b) asserts the consequent (no misordering log emitted) instead of the cause. Both work; pick the lower-friction shape.
- **Optional `LoggerMessage` debug log for "skipped due to endpoint metadata"** — not in scope. Add only if ops reports the lack hurts triage.

---

## Verification

The plan is complete when:

- U1: New attribute and extension member compile, are `[PublicAPI]`, mirror `[AllowMissingTenant]` shape.
- U2: Middleware short-circuits on the marker; existing tests still pass.
- U3: All 10 scenarios in U3 pass. `make test-project TEST_PROJECT=tests/Headless.Api.Tests.Integration` is green. No regressions in `TenantResolutionMiddlewareTests` or `TenantRequirementTests`.
- U4: Both doc files updated in the same commit; AUTHORING.md drift checklist passes; `[SkipTenantResolution]` vs. `[AllowMissingTenant]` distinction is stated explicitly in both.
- Project-wide: `make format-check`, `make build`, `make test` all green.
