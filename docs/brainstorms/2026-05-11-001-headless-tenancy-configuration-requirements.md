---
date: 2026-05-11
topic: headless-tenancy-configuration
---

# Headless Tenancy Configuration

## Summary

Add a first-class `AddHeadlessTenancy(...)` configuration surface that makes tenant posture discoverable across Headless packages while keeping each seam's authority local. HTTP tenant resolution moves from the current `UseTenantResolution()` happy path to a product-level `UseHeadlessTenancy()` middleware slot that applications place between their own authentication and authorization middleware.

---

## Problem Frame

Headless tenancy currently spans several package seams: API tenant resolution, Mediator tenant-required enforcement, Messaging tenant propagation and strict publish behavior, and EF tenant write guarding. A consuming application has to remember separate setup methods across those seams, understand which ones are service registrations versus middleware, and manually preserve the correct HTTP ordering.

This creates two classes of risk. First, the developer experience is fragmented: tenancy feels like a set of unrelated package switches instead of one framework capability. Second, partial wiring is easy: a host can configure tenant-aware services but omit the HTTP middleware slot, enable message propagation without a real ambient tenant source, or use EF filters without enabling write protection.

The framework is greenfield enough to choose a cleaner public story now. The important boundary is that discoverability should improve without turning tenancy into hidden magic: authentication remains application-owned, HTTP pipeline placement remains explicit, and each package keeps ownership of the invariant it enforces.

---

## Actors

- A1. Application developer: Configures tenancy for an API, worker, messaging host, or mixed backend service.
- A2. Package maintainer: Owns tenant behavior for one Headless seam, such as API, Mediator, Messaging, or EF.
- A3. Runtime host: Executes HTTP requests, commands, message publishes/consumes, and EF saves under the selected tenant posture.
- A4. Downstream planner/implementer: Turns this scope into package boundaries, API shape, validation behavior, tests, and docs.

---

## Key Flows

- F1. Configure tenant posture
  - **Trigger:** An application opts into Headless tenancy.
  - **Actors:** A1, A2
  - **Steps:** The developer calls one root configuration API, chooses the seams the app uses, and each seam records its configured tenant behavior into a shared posture record.
  - **Outcome:** The app's tenant posture is visible from one configuration block while enforcement remains package-owned.
  - **Covered by:** R1, R2, R3, R4, R5

- F2. Apply HTTP tenant resolution
  - **Trigger:** An HTTP app configured claim-based tenant resolution and starts its middleware pipeline.
  - **Actors:** A1, A3
  - **Steps:** The app applies Headless defaults, then app-owned authentication, then Headless tenancy, then app-owned authorization. Headless tenancy applies the configured HTTP tenant-resolution middleware.
  - **Outcome:** Tenant context is resolved in the correct HTTP slot without requiring the developer to call the lower-level tenant-resolution middleware directly.
  - **Covered by:** R6, R7, R8, R9, R10

- F3. Detect incomplete tenant wiring
  - **Trigger:** The app starts or a test verifies the tenant posture.
  - **Actors:** A1, A3, A4
  - **Steps:** Headless reads the configured posture, checks whether required service and pipeline pieces are present, and reports missing or inconsistent wiring with seam-specific diagnostics.
  - **Outcome:** Partial tenancy setup becomes visible before it causes runtime tenant isolation drift.
  - **Covered by:** R4, R11, R12, R13

---

## Requirements

**Composition root**
- R1. The framework must expose a first-class `AddHeadlessTenancy(...)` root configuration surface for tenant posture.
- R2. The root configuration surface must be available from a neutral tenancy package rather than being owned by the API package or folded into Core.
- R3. The root must act as an extension bus: API, Mediator, Messaging, EF, and future tenant-aware packages contribute their own seam-specific configuration extensions.
- R4. The root must produce a shared tenant posture record that downstream seams and diagnostics can inspect.
- R5. The root must not centralize seam enforcement logic; each package remains responsible for the behavior and failure modes it owns.

**HTTP pipeline**
- R6. HTTP tenant resolution must move out of the V1 happy path's lower-level `UseTenantResolution()` call and into a product-level `UseHeadlessTenancy()` middleware slot.
- R7. Applications must continue to own `UseAuthentication()` and `UseAuthorization()`; Headless tenancy APIs must not call either middleware internally.
- R8. The documented HTTP order must be Headless defaults, app-owned authentication, Headless tenancy, app-owned authorization, then endpoint mapping.
- R9. `UseHeadlessTenancy()` must apply HTTP tenant resolution only when HTTP tenant resolution was configured in the tenant posture.
- R10. If HTTP tenant resolution is configured but `UseHeadlessTenancy()` is omitted or placed in an invalid order that the framework can detect, the framework must surface a clear diagnostic.

**Seam behavior**
- R11. The API seam must own claim-based HTTP tenant resolution configuration.
- R12. The Mediator seam must own tenant-required request-boundary behavior and its explicit opt-out model.
- R13. The Messaging seam must own tenant propagation and strict publish/consume policy decisions.
- R14. The EF seam must own tenant write guarding, cross-tenant write failures, and scoped write-guard bypass behavior.
- R15. A generic tenant bypass must not become the main abstraction; generic visibility is acceptable, but bypass authority must stay local to the seam being bypassed.

**Safety and diagnostics**
- R16. Tenant diagnostics must report the configured posture by seam: configured, not configured, enforcing, propagating, guarded, or intentionally bypassable where applicable.
- R17. Validation must detect the highest-value partial-wiring failures, including configured HTTP tenant resolution without the HTTP tenancy middleware slot and messaging propagation without a real ambient tenant source.
- R18. Validation and diagnostics must distinguish missing tenant context from cross-tenant or trust-boundary failures when the owning seam already makes that distinction.
- R19. Diagnostics must be actionable without exposing tenant-owned entity values, message payload data, secrets, tokens, or other PII-bearing content.

**Documentation and migration**
- R20. Public docs and package READMEs must present `AddHeadlessTenancy(...)` plus `UseHeadlessTenancy()` as the primary tenancy setup path.
- R21. Existing lower-level tenant APIs may remain as advanced or compatibility surfaces, but they must not be the documented happy path.
- R22. Documentation must explain that `AddHeadlessInfrastructure()` registers base infrastructure only and does not enable tenant posture.
- R23. Documentation must explain why authentication and authorization middleware stay application-owned.

---

## Acceptance Examples

- AE1. **Covers R1, R3, R5.** Given an app uses API, Mediator, Messaging, and EF tenancy, when the developer opens its startup configuration, the tenant posture is visible from one root configuration block while each seam still names its own behavior.
- AE2. **Covers R6, R7, R8, R9.** Given HTTP tenant resolution is configured, when the app applies Headless defaults, authentication, Headless tenancy, and authorization in order, tenant context is resolved during HTTP requests without calling the lower-level tenant-resolution middleware.
- AE3. **Covers R7.** Given an app has custom authentication setup, when it calls Headless tenancy APIs, Headless does not add authentication or authorization middleware on the app's behalf.
- AE4. **Covers R10, R17.** Given HTTP tenant resolution is configured but the app omits the Headless tenancy middleware slot, when validation or startup diagnostics run, the app receives an actionable diagnostic instead of silently running at host scope.
- AE5. **Covers R13, R17.** Given messaging tenant propagation is configured without a real ambient tenant source, when startup validation runs, the app receives a diagnostic that propagation would not carry a tenant.
- AE6. **Covers R14, R15.** Given EF tenant write guarding is configured, when a host-level maintenance path needs a bypass, it uses the EF-owned scoped bypass rather than a generic cross-seam bypass.
- AE7. **Covers R20, R21.** Given a new consumer follows the package documentation, when they configure tenancy, they see the root configuration API and Headless tenancy middleware slot as the primary flow, not the legacy lower-level HTTP tenant-resolution call.

---

## Success Criteria

- A new Headless application can understand its tenant posture from one configuration block instead of remembering unrelated setup methods across packages.
- HTTP tenant resolution no longer requires the user-facing `UseTenantResolution()` happy-path call.
- The framework improves safety by making partial tenant wiring visible before production traffic depends on it.
- Authentication and authorization remain clearly application-owned, avoiding hidden ASP.NET middleware behavior.
- Planning can proceed without inventing product behavior: it only needs to decide package boundaries, API shape, diagnostics mechanics, tests, and migration details.

---

## Scope Boundaries

- Do not enable tenant posture implicitly from `AddHeadlessInfrastructure()`.
- Do not call `UseAuthentication()` or `UseAuthorization()` from Headless tenancy methods.
- Do not collapse all tenancy configuration into a flat mega-options object.
- Do not introduce a generic tenant bypass as the primary escape hatch.
- Do not make presets such as `ApiStrict` or `EndToEndStrict` the primary V1 deliverable.
- Do not remove existing lower-level APIs unless planning confirms the compatibility and migration impact.
- Do not require non-HTTP hosts to reference the API package only to configure tenant posture.
- Do not expose entity values, message payload values, tokens, secrets, or PII in diagnostics.

---

## Key Decisions

- Neutral tenancy orchestration package: The root belongs outside API and Core so non-HTTP hosts can configure tenant posture without taking an ASP.NET dependency, while Core stays focused on primitives.
- Extension-bus root: The root improves discoverability, but packages keep ownership of their own behavior and validation.
- Product-level HTTP middleware name: `UseHeadlessTenancy()` replaces the happy-path concept of `UseTenantResolution()` and better matches the framework-level configuration surface.
- App-owned auth middleware: Headless does not call authentication or authorization internally because scheme configuration, branching, and ordering are application responsibilities in ASP.NET Core.
- Manifest-backed safety: A shared posture record is needed so diagnostics and validation can reason about partial wiring without centralizing all enforcement in one package.

---

## Dependencies / Assumptions

- `ICurrentTenant` and `ICurrentTenantAccessor` remain the shared ambient tenant primitives.
- `AddHeadlessInfrastructure()` and EF context services may continue registering the neutral ambient tenant implementation, but that registration is not the same as enabling tenant posture.
- Existing seam behavior remains valid: Mediator marker-based opt-out, Messaging propagation/strict publish behavior, and EF write guard with scoped bypass.
- The project accepts a new neutral package if planning confirms the dependency graph is clean.
- ASP.NET middleware ordering cannot be fully inferred from service registration alone; the HTTP pipeline still needs an explicit app-level middleware slot.

---

## Outstanding Questions

### Deferred to Planning

- [Affects R2][Technical] What exact package name and dependency graph best fits the neutral tenancy orchestration package?
- [Affects R4, R16][Technical] What shape should the shared tenant posture record expose so seams can contribute status without tight coupling?
- [Affects R10, R17][Technical] How much HTTP middleware omission or ordering can be detected reliably without false positives?
- [Affects R21][Technical] Should the lower-level tenant-resolution middleware be kept, renamed, marked obsolete, or hidden from docs only?
- [Affects R13][Technical] Should V1 keep messaging strictness at publish-only semantics or introduce producer/consumer policy names during the same change?
