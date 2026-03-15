# Proposal: Unify Jobs and Messaging Dashboards

## Summary

Use `Headless.Jobs.Dashboard` as the base dashboard shell and merge the Messaging dashboard into that shell incrementally.

Do not fold Messaging backend concerns into the Jobs domain model. The dashboard shell can be shared, but Messaging should remain a separate feature module with its own services, routes, and optional Kubernetes integration.

## Problem

The repository currently has two dashboard implementations:

- `src/Headless.Jobs.Dashboard`
- `src/Headless.Messaging.Dashboard`

They solve similar problems with different stacks and conventions:

- embedded SPA hosting
- backend APIs for operational data
- realtime or polling updates
- dashboard auth and routing
- package-specific operational UI

Keeping both dashboards separate increases maintenance cost:

- duplicated dashboard hosting logic
- duplicated auth/routing/static asset concerns
- divergent frontend patterns
- inconsistent operator experience

## Current State

### Jobs Dashboard

`Headless.Jobs.Dashboard` is the better foundation:

- modern embedded SPA pattern
- cleaner endpoint organization
- SignalR support for live notifications
- stronger auth/session model
- better shell for future operational pages

It already behaves like a host application, not just a feature page.

### Messaging Dashboard

`Headless.Messaging.Dashboard` contains Messaging-specific operational logic that must be preserved:

- node discovery
- gateway proxy behavior
- metrics listener integration
- route/action provider model
- Kubernetes add-on package in `Headless.Messaging.Dashboard.K8s`

This package has valuable domain behavior, but its dashboard shell is older and overlaps with the Jobs dashboard shell.

## Recommendation

Adopt this direction:

1. Treat `Headless.Jobs.Dashboard` as the dashboard host and frontend shell.
2. Move Messaging dashboard UI into that shell as a Messaging module.
3. Keep Messaging backend services and operational logic in Messaging packages.
4. Avoid making Messaging depend on Jobs execution/persistence/runtime abstractions.

In short:

- shared shell: based on Jobs dashboard
- feature modules: Jobs, Messaging
- domain backends: stay separate

## Target Architecture

### Phase 1 Target

Keep package changes pragmatic:

- `Headless.Jobs.Dashboard`
  - dashboard shell
  - auth middleware/session model
  - static asset hosting
  - navigation/layout
  - shared frontend app
  - shared realtime plumbing
  - Jobs pages/APIs
  - Messaging pages host slot

- `Headless.Messaging.Dashboard`
  - Messaging API endpoints/services/adapters
  - node discovery
  - gateway proxy
  - metrics listeners
  - compatibility facade for old registration methods

- `Headless.Messaging.Dashboard.K8s`
  - Kubernetes-specific discovery/integration
  - plugs into Messaging dashboard module

### Phase 2 Target

If a third operational dashboard appears, extract the shell into a neutral package:

- `Headless.Dashboard` or `Headless.Operations.Dashboard`

That package would own:

- dashboard host pipeline
- auth/session model
- shared frontend shell
- navigation registration
- module registration
- shared asset embedding conventions

Then:

- `Headless.Jobs.Dashboard` becomes a Jobs module package
- `Headless.Messaging.Dashboard` becomes a Messaging module package

This extraction should happen only if reuse justifies it. It is not required for the first merge.

## Why This Direction

### Benefits

- one operator experience instead of two dashboards
- one embedded SPA shell to maintain
- one auth and routing model
- one place for shared UI components and frontend infrastructure
- preserves Messaging-specific backend behavior
- lower risk than rewriting both dashboards into a new platform first

### Why Not Merge Messaging Directly Into Jobs

That would create the wrong dependency direction.

Messaging should not need Jobs runtime concepts to expose its dashboard. The UI host can come from Jobs today, but the Messaging domain should stay isolated behind its own services and registrations.

## Proposed Package Model

Short term:

- keep `Headless.Jobs.Dashboard`
- keep `Headless.Messaging.Dashboard`
- make Messaging render inside the Jobs-hosted shell

Suggested registration shape:

- `AddDashboard(...)` remains the main host registration
- Jobs registers a `JobsDashboardModule`
- Messaging registers a `MessagingDashboardModule`

Possible extension model:

```csharp
builder.AddDashboard(dashboard =>
{
    dashboard.AddJobsModule();
    dashboard.AddMessagingModule();
});
```

This is a proposal, not a required final API shape. The key requirement is module-style composition.

## Frontend Proposal

Use the Jobs dashboard frontend as the single SPA.

Messaging pages should be migrated into that app:

- shared layout
- shared auth/session handling
- shared navigation
- shared HTTP client conventions
- shared state/update strategy

Messaging-specific screens can live under routes such as:

- `/messaging/overview`
- `/messaging/nodes`
- `/messaging/queues`
- `/messaging/consumers`

Jobs keeps routes such as:

- `/jobs/overview`
- `/jobs/scheduled`
- `/jobs/processing`
- `/jobs/failed`

## Backend Proposal

Unify hosting concerns, not domain concerns.

Shared host responsibilities:

- auth
- path base handling
- static files
- SPA fallback
- shared endpoint conventions
- shared hub/realtime registration

Messaging-specific responsibilities remain in Messaging:

- metrics collection
- node discovery
- gateway proxying
- cluster/Kubernetes awareness
- Messaging queries and operational commands

Jobs-specific responsibilities remain in Jobs:

- job queries
- job retries/cancellation actions
- scheduling views
- execution history and operational controls

## Migration Plan

### Phase 1: Define Shell Boundaries

- identify Jobs dashboard pieces that are shell-specific
- isolate shared auth, base path, static hosting, and SPA fallback logic
- define a module registration contract for dashboard sections

### Phase 2: Rehost Messaging UI

- move Messaging frontend pages/components into the Jobs dashboard SPA
- add Messaging navigation and route groups
- keep Messaging APIs in Messaging packages

### Phase 3: Bridge Messaging Backend

- adapt `Headless.Messaging.Dashboard` registrations to plug into the shared shell
- preserve existing Messaging services and K8s integration
- map existing Messaging endpoints into the unified dashboard structure

### Phase 4: Compatibility Layer

- keep existing `UseMessagingDashboard()` or equivalent entry points working temporarily
- internally route them to the unified shell/module registration
- mark old standalone shell APIs obsolete after one release cycle

### Phase 5: Optional Extraction

- if reuse expands, extract the shell to `Headless.Dashboard` / `Headless.Operations.Dashboard`

## Compatibility Strategy

Preserve compatibility where possible:

- keep existing Messaging service registrations working
- keep existing base paths or redirect them
- avoid breaking cluster/K8s discovery integrations
- keep old package alive as a facade during transition

Likely compatibility choices:

- old Messaging dashboard URL redirects to new unified route
- old registration extensions call the new module registration internally
- package deprecation happens after the unified dashboard is stable

## Risks

### Frontend Build Divergence

The two dashboards do not currently share the same frontend conventions. Migration will require aligning build, routing, and asset embedding.

### Realtime Model Differences

Jobs already uses SignalR-style notifications. Messaging may still depend more heavily on polling or event listener flows. A unified shell must support both without forcing a rewrite in the first pass.

### Dependency Drift

If Messaging module code starts referencing Jobs runtime types, the architecture will regress. The merge should be shell-first, not domain-coupling.

### URL/Auth Breakage

Existing dashboard paths and auth policies may differ. These need explicit migration and redirect handling.

## Open Questions

Before implementation, confirm:

1. Should the unified operator experience live under one root path, or keep separate roots inside the same SPA?
2. Do we want a neutral package extraction now, or only after the first successful merge?
3. How long should the old Messaging dashboard registration API remain supported?
4. Should Messaging adopt SignalR for live updates, or keep its current polling/listener model initially?

## Final Recommendation

Proceed with a phased merge where:

- `Headless.Jobs.Dashboard` becomes the shared dashboard shell
- Messaging becomes a module hosted inside that shell
- Messaging backend logic stays in Messaging packages
- a neutral shared dashboard package is deferred until reuse proves necessary

This is the lowest-risk path with the highest immediate consolidation value.
