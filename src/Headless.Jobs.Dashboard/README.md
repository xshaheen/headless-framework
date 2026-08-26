# Headless.Jobs.Dashboard

Embedded web monitoring UI for `Headless.Jobs` with pluggable authentication and real-time cluster updates.

## Problem Solved

Provides operational visibility into the Jobs scheduler — job queues, execution history, live cluster nodes, retry/failure details — without requiring a separate monitoring service. The dashboard is embedded in the host application and mounted under a configurable URL path.

## Key Features

- **Embedded SPA**: served from the host process, no separate deployment.
- **Authentication options**: `WithBasicAuth(username, password)`, `WithApiKey(apiKey)`, `WithHostAuthentication(policy?)` (delegates to host app's auth), or explicit no-auth mode for isolated development dashboards.
- **Read / admin permission split**: under `WithHostAuthentication`, every route is classified as read, tenant-row mutation, or admin and checked against the `JobsDashboardPermissions.Read` / `JobsDashboardPermissions.Admin` claim values; time-job update, delete, and cancel additionally authorize against the **persisted** row tenant.
- **Safe host-auth handoff**: fragment-delivered access tokens are removed from the URL, then validated only after the SPA initializes the host authentication configuration.
- **Predictable timestamp display**: explicit ISO UTC offsets are preserved, legacy zone-less values are treated as UTC, and invalid values render empty instead of `NaN`.
- **Responsive operational layout**: content cards shrink within mobile viewports while wide data tables retain their own overflow boundary.
- **Live cluster view**: `GET /api/nodes` returns live node projections from `Headless.Coordination` membership; `NodeJoined` / `NodeLeft` / `NodeSuspected` push updates over SignalR — no polling required.
- **Error monitoring**: surfaces failed, cancelled, and skipped jobs; retry counts; execution timings; exception messages.
- **Fluent builder**: `SetBasePath(path)`, `SetBackendDomain(domain)`, `SetCorsOrigins(origins)`, `SetCorsPolicy(policy)`.
- **Pair with OpenTelemetry**: Dashboard for operational triage; the built-in OpenTelemetry instrumentation in `Headless.Jobs.Core` (`AddOpenTelemetryInstrumentation()` + `AddJobsInstrumentation()`) for trace-level diagnostics.

## Design Notes

The dashboard exposes operational endpoints that can create, update, delete, run, cancel, start, stop, and restart jobs. Authentication must be chosen explicitly — if no auth method (including `WithNoAuth()`) is called, the host fails to start, so the dashboard never ships publicly by omission. Treat `WithNoAuth()` as development-only unless the dashboard is isolated behind trusted network controls; production deployments should use `WithHostAuthentication(...)`, `WithBasicAuth(...)`, or `WithApiKey(...)`. No CORS policy is applied by default (same-origin only); use `SetCorsOrigins(...)` when the SPA is served cross-origin.

Dashboard API inputs are bounded: paginated queries accept page sizes from 1 through 100, JSON request bodies are limited to 1 MiB, and batch deletion accepts at most 500 IDs. Collection endpoints use the paginated routes; the legacy all-record `time-jobs`, `cron-jobs`, and `cron-job-occurrences/{cronJobId}` routes are not exposed.

### Permission model

Every route carries exactly one access class; a single authorizer enforces it for the `/api` group (endpoint filter), the tenant-row handlers, and the SignalR hub, so the rules cannot drift. Under host authentication the permissions are the claim values `JobsDashboardPermissions.Read` (`headless.jobs.read`) and `JobsDashboardPermissions.Admin` (`headless.jobs.admin`) on `HttpContext.User`, read from the `permission` claim type by default (`WithPermissionClaimType(...)` overrides it). Admin implies read.

| Class | Routes | Requirement |
| --- | --- | --- |
| Anonymous | `GET /api/auth/info`, `POST /api/auth/validate` | none — authentication bootstrap |
| Read | every `GET` route (`options`, time/cron listings and graphs, occurrences, `job-request/id`, `job-functions`, `job-host/next-job`, `job-host/status`, `job/statuses/*`, `job/machine/jobs`, `nodes`) and the `/job-notification-hub` connection | `read` or `admin` |
| Tenant-row mutation | `PUT /api/time-job/update`, `DELETE /api/time-job/delete`, `POST /api/job/cancel` | `read` plus the caller's resolved `ICurrentTenant.Id` equal to the **persisted** row `TenantId`, or `admin` |
| Admin | `POST /api/time-job/add`, `DELETE /api/time-job/delete-batch`, every `cron-job*` mutation (`add`, `update`, `run`, `delete`, occurrence `delete`), `POST /api/job-host/{stop,start,restart}` | `admin` |

Tenant-row rules: the stored row is authoritative. A submitted `TenantId` never grants access, and an update body cannot move a job between tenants (the handler pins the persisted value). A caller without an ambient tenant never matches a tenant-owned row, and a system-scope time job (`TenantId == null`) is admin-only. Cron definitions and occurrences are always system scope, so there is no same-tenant shortcut for cron mutations. A cross-tenant attempt returns `403` before any manager or scheduler call. Tenant resolution comes from the host's `ICurrentTenant` (for example `AddHeadlessTenancy(t => t.Http(h => h.ResolveFromClaims()))`); the dashboard never derives it from the request body.

Status codes: `401` for an unauthenticated caller (host authorization middleware in Host mode, `AuthMiddleware` otherwise), `403` for an authenticated caller that lacks the route's permission or fails the tenant assertion. The hub aborts the connection in both cases.

Compatibility: the single-credential Basic, API-key, and custom modes cannot express per-user permissions, so a successfully authenticated caller in those modes keeps the historical all-access behavior and is treated as admin. `WithNoAuth()` remains an explicit all-access opt-out for development or trusted networks. Only Host mode exercises the read/admin split.

## Installation

```bash
dotnet add package Headless.Jobs.Dashboard
```

## Quick Start

```csharp
using Headless.Jobs;

builder
    .Services.AddHeadlessJobs()
    .AddDashboard(dashboard =>
    {
        dashboard.SetBasePath("/jobs-dashboard");
        dashboard.WithHostAuthentication(); // or WithBasicAuth / WithApiKey
    });

// No app.MapJobs() or app.UseJobs() — the dashboard middleware is injected via IStartupFilter.
var app = builder.Build();
app.Run();
```

## Configuration

```csharp
builder
    .Services.AddHeadlessJobs()
    .AddDashboard(dashboard =>
    {
        dashboard.SetBasePath("/jobs");
        dashboard.SetBackendDomain("https://api.example.com");
        dashboard.SetCorsOrigins("https://admin.example.com"); // needed only when the SPA is cross-origin

        // Authentication — required, pick one:
        dashboard.WithBasicAuth("admin", "secret");
        dashboard.WithApiKey("my-api-key");
        dashboard.WithHostAuthentication();
        dashboard.WithHostAuthentication("AdminPolicy");
        dashboard.WithPermissionClaimType("scope"); // host mode only; default "permission"
        // Or opt out explicitly with dashboard.WithNoAuth() — isolated development environments only.
    });
```

Auth detection is automatic: explicit `WithNoAuth()` → public; basic auth → username/password login UI; API key → bearer token; host auth → delegates to the host's authentication middleware and then applies the `JobsDashboardPermissions` read/admin split.

## Dependencies

- `Headless.Jobs.Abstractions`
- `Headless.Jobs.Core`
- `Headless.Dashboard.Authentication` (shared with `Headless.Messaging.Dashboard`)
- `Headless.Extensions`

## Side Effects

- Mounts dashboard HTTP API and SignalR hub under `SetBasePath` path via `IStartupFilter` (no explicit `app.Use…` call needed).
- Subscribes to `Headless.Coordination` membership events for live-node push updates.
- Serves embedded frontend SPA assets; requires Node 22 on `PATH` when building from source.
- Exposes mutating operational endpoints; configure authentication and CORS before exposing the dashboard outside an isolated development environment.
