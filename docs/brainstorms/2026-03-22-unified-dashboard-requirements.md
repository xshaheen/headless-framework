---
date: 2026-03-22
topic: unified-dashboard
---

# Unified Dashboard

## Problem Frame

The framework ships two independent dashboard packages (`Headless.Jobs.Dashboard`, `Headless.Messaging.Dashboard`) that share auth infrastructure but duplicate layout, SPA build pipeline, stores, and configuration patterns. Consumers who use both must configure two separate dashboards at different base paths, with no unified view of their system. This increases adoption friction and maintenance cost.

## Requirements

- R1. Replace the two existing dashboard packages with a **core + plugins** architecture:
  - `Headless.Dashboard` — core package owning the SPA shell, auth, shared layout, and module discovery
  - `Headless.Dashboard.Jobs` — jobs-specific API endpoints (no frontend assets)
  - `Headless.Dashboard.Messaging` — messaging-specific API endpoints (no frontend assets)
  - `Headless.Dashboard.Messaging.K8s` — K8s node discovery extension (unchanged scope, updated dependency)

- R2. The **single Vue SPA** lives in `Headless.Dashboard`. It contains all views for all modules. At runtime, a `/api/modules` endpoint tells the frontend which modules are active. Inactive module sections are hidden from navigation and routes.

- R3. **Adaptive UI**: when only Jobs is registered, only Jobs sections appear. When only Messaging is registered, only Messaging sections appear. When both are registered, both appear. The dashboard must never show UI for unregistered modules.

- R4. **Sidebar navigation** with collapsible sections per module:
  - Overview (combined status when multiple modules active, module-specific when single)
  - Jobs section: Time Jobs, Cron Jobs, Host
  - Messaging section: Published, Received, Subscribers, Nodes

- R5. **Explicit core-first DI registration**:
  ```csharp
  // 1. Core dashboard (auth + base path configured here)
  builder.Services.AddHeadlessDashboard(d => {
      d.WithBasicAuth("admin", "pw");
      d.SetBasePath("/dashboard");
  });

  // 2. Plugins attach (no auth config)
  builder.Services.AddHeadlessJobs(o => o.AddDashboard());
  builder.Services.AddHeadlessMessaging(o => o.UseDashboard());
  ```

- R6. `Headless.Dashboard.Authentication` is **absorbed into `Headless.Dashboard`** — no reason for a separate auth-only package when auth is always part of the core dashboard.

- R7. Old packages (`Headless.Jobs.Dashboard`, `Headless.Messaging.Dashboard`) are **removed**. This is a breaking change. No compatibility shims.

- R8. Each plugin registers its API endpoints and a module descriptor with the core. The core exposes the `/api/modules` endpoint by aggregating registered descriptors.

- R9. Real-time strategies remain plugin-specific: Jobs keeps SignalR, Messaging keeps polling. The core does not dictate real-time approach.

## Success Criteria

- Consumer can configure one dashboard and see both Jobs and Messaging in a single UI
- Consumer using only Jobs (or only Messaging) gets a working dashboard with no unused module UI
- Auth is configured once, not per-module
- Frontend is a single SPA build with one build pipeline
- Existing Jobs and Messaging dashboard functionality is preserved (no feature regression)

## Scope Boundaries

- **Not in scope**: New dashboard features beyond what exists today. This is a structural merge, not a feature expansion.
- **Not in scope**: Changing the Vue framework, component library, or build tooling. Reuse existing Vue 3 + Pinia + TypeScript stack.
- **Not in scope**: Merging Jobs and Messaging backend logic — only the dashboard layer merges.
- **Not in scope**: Overview page design — can be a simple combined status initially; richer overview is a future iteration.

## Key Decisions

- **Core + Plugins over single package**: Avoids forcing transitive dependency on both subsystems when consumer only uses one.
- **Single SPA in core over micro-frontends**: Pragmatic — one build, dead code for inactive modules is just static files with negligible size impact.
- **Explicit core-first DI over auto-registration**: Clearer ownership of shared config (auth, base path). Consumer calls `AddHeadlessDashboard` first.
- **Absorb auth package into core**: `Headless.Dashboard.Authentication` has no standalone use case outside the dashboard.
- **Breaking change accepted**: Greenfield project, cleaner API > backward compat.

## Frontend Merge Strategy

- R10. The merged SPA uses a **module-based folder structure**:
  - `src/core/` — deduplicated shared code (components, composables, services, stores, layout, utilities)
  - `src/modules/jobs/` — jobs views, components, stores, services, routes
  - `src/modules/messaging/` — messaging views, components, stores, services, routes
  - Each module exports a `routes.ts` that the core router conditionally registers based on `/api/modules`

- R11. **Deduplicated shared code** (currently duplicated across both SPAs):
  - Components: AuthHeader, ConfirmDialog, GlobalAlerts, PaginationFooter, TableSkeleton, DashboardLayout
  - Composables: useAlert, useDialog, usePagination
  - Services: auth (parameterized by config), HTTP client (parameterized by base URL)
  - Stores: authStore, alertStore
  - Utilities: dateTimeParser, pathResolver

- R12. **Dependencies unified** into one `package.json`. Version alignment: echarts 5.6, vue-echarts 7.0. Jobs-only deps (SignalR, cronstrue, maska, yup) and Messaging-only deps (json-bigint) coexist — tree-shaking handles unused code paths.

- R13. **One Vite build pipeline** producing a single `dist/` embedded in the `Headless.Dashboard` NuGet package.

## Dependencies / Assumptions

- Both existing dashboard frontends use Vue 3 + Pinia — merge is feasible without framework migration
- ~60% of frontend code is duplicated and can be consolidated into `src/core/`
- The core package will embed the full SPA; plugin packages contribute only .NET endpoints
- Module discovery is runtime (DI-based), not build-time

## Outstanding Questions

### Deferred to Planning

- [Affects R5][Technical] How to fail fast if `o.AddDashboard()` is called without prior `AddHeadlessDashboard()`? Options: throw at registration time vs. throw at app start via `IStartupFilter` validation.
- [Affects R8][Needs research] Design the module descriptor interface — what metadata does each plugin provide (name, routes, icon, order)?
- [Affects R10-R13][Technical] Detailed file-by-file merge plan — which components need parameterization vs direct reuse, auth service config object shape, router conditional registration implementation.
- [Affects R4][Technical] Sidebar component design — collapsible sections, active state, responsive behavior.
- [Affects R7][Technical] Migration path for demo apps — update all demos to new package structure.

## Next Steps

`/dev:plan` for structured implementation planning.
