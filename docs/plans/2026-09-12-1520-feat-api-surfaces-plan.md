---
title: "feat: First-Class API Surfaces (Audiences/Partitions)"
type: feat
date: 2026-09-12
artifact_contract: x-unified-plan/v1
artifact_readiness: implementation-ready
product_contract_source: x-plan-bootstrap
execution: code
---

# First-Class API Surfaces (Audiences/Partitions) - Plan

## Goal Capsule

Introduce first-class **API Surfaces** (audience-oriented route partitions) into `headless-framework`. Provide a unified way to configure multi-audience APIs (e.g. Console, Partner, Public, Customer) with route prefixes, default authorization policies, tenant postures, OpenTelemetry activity tagging, and multi-document OpenAPI generation for both MVC Controllers and Minimal APIs.

- **Authority:** User request to generalize the API Surface pattern across the Headless ecosystem.
- **Baseline:** `origin/main` at commit `954ee2c7f` on 2026-09-12.
- **Execution:** 5 bounded implementation units (U1 through U5), executed via `x-code` and culminating in an open pull request.
- **Stop condition:** Inability to compile or pass quality analyzer gates against the greenfield conventions of `headless-framework`.

---

## Product Contract

### Summary

Modern APIs frequently serve distinct audiences with conflicting requirements: an administrative console requiring admin policies and no tenancy, a tenant portal requiring tenant resolution, and a public surface allowing anonymous access. Currently, downstream consumers must manually build MVC conventions, pre-auth middleware, and NSwag multi-document registrations. 

This feature introduces `Headless.Api.Surfaces` primitives to configure surfaces once, automatically binding routing, authorization, telemetry tags, and OpenAPI documents across MVC and Minimal APIs.

### Problem Frame

1. **Scattered Configuration:** Route prefixes, authorization policies, tenant requirements, and OpenAPI document groups are defined independently across controllers and pipeline setups.
2. **Telemetry Gaps on Rejection:** If authentication or authorization fails (401/403), the pipeline aborts before endpoint filters run, leaving traces missing audience/surface context.
3. **OpenAPI Multi-Document Friction:** Generating separate OpenAPI specifications (crucial for isolated client SDK generation via Orval/Kiota) requires complex manual NSwag loops and document processor plumbing.

### Requirements

- **R1 (Abstractions & Options):** Expose `IApiSurfaceMetadata`, `ApiSurfaceDescriptor`, `ApiSurfaceFeature`, and `SurfaceTenancyPosture` in `Headless.Api.Abstractions` / `Headless.Api.Core`.
- **R2 (Core Middleware & DI):** Provide `AddHeadlessApiSurfaces` and `UseHeadlessApiSurfaces` in `Headless.Api.Core`. The middleware runs post-routing and pre-auth, tagging `Activity.Current` with `api.surface` and stamping `ApiSurfaceFeature` onto `HttpContext.Features`.
- **R3 (MVC Integration):** In `Headless.Api.Mvc`, provide `[ApiSurface("name")]` attribute and `ApiSurfaceConvention` that automatically applies route prefixes, injects `AuthorizeAttribute` for required policies into endpoint metadata, sets tenancy metadata, and sets `ApiExplorer.GroupName`.
- **R4 (Minimal API Integration):** In `Headless.Api.MinimalApi`, provide `MapSurfaceGroup(name, builder)` on `IEndpointRouteBuilder` to configure route group prefixes, authorization policies, tenancy metadata, and OpenAPI groups.
- **R5 (OpenAPI Generation):** In `Headless.OpenApi.Nswag`, provide `AddNswagOpenApiSurfaces` and `MapNswagOpenApiSurfaces` to automatically register and serve per-surface OpenAPI documents (`/openapi/{surface}.json`) and Swagger UI.
- **R6 (Zero Performance Overhead):** Surface features and lookups MUST use immutable, frozen collections (`FrozenDictionary`) to ensure zero-allocation lookups during request execution.

### Scope Boundaries

- No breaking changes to existing single-document `AddNswagOpenApi` calls; surfaces are opt-in.
- Cross-surface breach detection heuristics stay extensible via user callbacks / events rather than baking domain-specific role/claim assumptions into framework core.

---

## Technical Design

```
                     ┌────────────────────────────────┐
                     │          UseRouting()          │
                     └───────────────┬────────────────┘
                                     │
                                     ▼
                     ┌────────────────────────────────┐
                     │     UseHeadlessApiSurfaces()   │
                     │  - Reads endpoint metadata     │
                     │  - Sets HttpContext.Features   │
                     │  - Tags Activity: api.surface  │
                     └───────────────┬────────────────┘
                                     │
                                     ▼
                     ┌────────────────────────────────┐
                     │     UseAuthentication()        │
                     │     UseHeadlessTenancy()       │
                     │     UseAuthorization()         │
                     │  (Denials still have tags!)    │
                     └───────────────┬────────────────┘
                                     │
                                     ▼
                     ┌────────────────────────────────┐
                     │          Endpoints             │
                     │  - MVC Controllers ([Surface]) │
                     │  - Minimal API (MapSurfaceGroup)│
                     └────────────────────────────────┘
```

---

## Implementation Units

### U1. Core Abstractions, Options, and Observation Middleware
- **Goal:** Define surface models and the pre-auth observation middleware.
- **Requirements:** R1, R2, R6
- **Files:**
  - `src/Headless.Api.Abstractions/Surfaces/IApiSurfaceMetadata.cs`
  - `src/Headless.Api.Abstractions/Surfaces/SurfaceTenancyPosture.cs`
  - `src/Headless.Api.Abstractions/Surfaces/ApiSurfaceFeature.cs`
  - `src/Headless.Api.Core/Surfaces/ApiSurfaceDescriptor.cs`
  - `src/Headless.Api.Core/Surfaces/ApiSurfaceOptions.cs`
  - `src/Headless.Api.Core/Surfaces/ApiSurfaceMiddleware.cs`
  - `src/Headless.Api.Core/Surfaces/SetupApiSurfaces.cs`
- **Approach:**
  1. Create metadata interfaces in `Headless.Api.Abstractions`.
  2. Implement `ApiSurfaceOptions` with a fluent `AddSurface(name, Action<ApiSurfaceDescriptor>)` API.
  3. Implement `ApiSurfaceMiddleware` using `FrozenDictionary<string, ApiSurfaceFeature>` for zero allocation. Set `Activity.Current?.SetTag("api.surface", feature.Name)`. Tag unmapped endpoints as `unknown` and non-surface endpoints as `infrastructure`.

### U2. MVC Controller Model Convention and Attribute
- **Goal:** Declarative surface binding on MVC controllers.
- **Requirements:** R3
- **Files:**
  - `src/Headless.Api.Mvc/Surfaces/ApiSurfaceAttribute.cs`
  - `src/Headless.Api.Mvc/Surfaces/ApiSurfaceConvention.cs`
  - `src/Headless.Api.Mvc/Setup.cs`
- **Approach:**
  1. Add `[ApiSurface(string name)]` attribute implementing `IApiSurfaceMetadata`.
  2. Implement `ApiSurfaceConvention : IControllerModelConvention` that resolves the configured surface descriptor, prepends `RoutePrefix` if present, adds `AuthorizeAttribute(policy)` to action selector metadata if configured, sets `ApiExplorer.GroupName = descriptor.GroupName`, and adds tenancy metadata if configured.
  3. Wire the convention in MVC options via `AddHeadlessMvc` / setup extensions.

### U3. Minimal API Surface Group Extensions
- **Goal:** First-class route group mapping for Minimal APIs.
- **Requirements:** R4
- **Files:**
  - `src/Headless.Api.MinimalApi/Surfaces/SurfaceEndpointRouteBuilderExtensions.cs`
- **Approach:**
  1. Implement `MapSurfaceGroup(this IEndpointRouteBuilder endpoints, string surfaceName, Action<RouteGroupBuilder> configure)`.
  2. Retrieve the `ApiSurfaceDescriptor` from DI options.
  3. Prepend route prefix, apply `WithGroupName(descriptor.GroupName)`, add `IApiSurfaceMetadata` to group metadata, and apply `.RequireAuthorization(descriptor.RequiredPolicy)` when configured.

### U4. Multi-Document OpenAPI Support in NSwag
- **Goal:** Automatically generate and host distinct OpenAPI documents per surface.
- **Requirements:** R5
- **Files:**
  - `src/Headless.OpenApi.Nswag/Surfaces/SetupNswagSurfaces.cs`
- **Approach:**
  1. Expose `AddNswagOpenApiSurfaces(this IServiceCollection services, Action<HeadlessNswagOptions>? setupHeadlessAction = null)` which reads registered `ApiSurfaceOptions` and calls `services.AddOpenApiDocument(...)` for each surface, setting `settings.ApiGroupNames = [surface.GroupName]`, `settings.DocumentName = surface.DocumentName`, and custom document title.
  2. Expose `MapNswagOpenApiSurfaces(this WebApplication app)` which mounts `/openapi/{surface.DocumentName}.json` for each surface and wires Swagger UI with all surface documents available in the selector.

### U5. Conformance & Verification Tests
- **Goal:** Comprehensive unit and integration test coverage across all layers.
- **Requirements:** R1-R6
- **Files:**
  - `tests/Headless.Api.Abstractions.Tests.Unit/Surfaces/SurfaceMetadataTests.cs`
  - `tests/Headless.Api.Composition.Tests.Unit/Surfaces/ApiSurfaceMiddlewareTests.cs`
  - `tests/Headless.Api.Mvc.Tests.Unit/Surfaces/ApiSurfaceConventionTests.cs`
  - `tests/Headless.Api.MinimalApi.Tests.Unit/Surfaces/MinimalApiSurfaceGroupTests.cs`
  - `tests/Headless.OpenApi.Nswag.Tests.Unit/Surfaces/NswagSurfaceGenerationTests.cs`
- **Approach:**
  1. Test metadata and options validation.
  2. Test middleware tag generation and feature population (including unknown and infrastructure fallbacks).
  3. Test MVC convention route prefixing and authorization metadata stamping.
  4. Test Minimal API route group metadata and prefix application.
  5. Test NSwag document generation per surface group.
  6. Run `make quality-analyzers` and `make format-check`.

---

## Verification & Quality Gates

- `make build` / `make build-project`
- `make test-project` for all affected test projects
- `make quality-analyzers` (zero warnings/errors)
- `make format-check`
