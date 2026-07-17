# Headless.Jobs.Dashboard

Embedded web monitoring UI for `Headless.Jobs` with pluggable authentication and real-time cluster updates.

## Problem Solved

Provides operational visibility into the Jobs scheduler — job queues, execution history, live cluster nodes, retry/failure details — without requiring a separate monitoring service. The dashboard is embedded in the host application and mounted under a configurable URL path.

## Key Features

- **Embedded SPA**: served from the host process, no separate deployment.
- **Authentication options**: `WithBasicAuth(username, password)`, `WithApiKey(apiKey)`, `WithHostAuthentication(policy?)` (delegates to host app's auth), or explicit no-auth mode for isolated development dashboards.
- **Live cluster view**: `GET /api/nodes` returns live node projections from `Headless.Coordination` membership; `NodeJoined` / `NodeLeft` / `NodeSuspected` push updates over SignalR — no polling required.
- **Error monitoring**: surfaces failed, cancelled, and skipped jobs; retry counts; execution timings; exception messages.
- **Fluent builder**: `SetBasePath(path)`, `SetBackendDomain(domain)`, `SetCorsOrigins(origins)`, `SetCorsPolicy(policy)`.
- **Pair with OpenTelemetry**: Dashboard for operational triage; the built-in OpenTelemetry instrumentation in `Headless.Jobs.Core` (`AddOpenTelemetryInstrumentation()` + `AddJobsInstrumentation()`) for trace-level diagnostics.

## Design Notes

The dashboard exposes operational endpoints that can create, update, delete, run, cancel, start, stop, and restart jobs. Authentication must be chosen explicitly — if no auth method (including `WithNoAuth()`) is called, the host fails to start, so the dashboard never ships publicly by omission. Treat `WithNoAuth()` as development-only unless the dashboard is isolated behind trusted network controls; production deployments should use `WithHostAuthentication(...)`, `WithBasicAuth(...)`, or `WithApiKey(...)`. No CORS policy is applied by default (same-origin only); use `SetCorsOrigins(...)` when the SPA is served cross-origin.

Dashboard API inputs are bounded: paginated queries accept page sizes from 1 through 100, JSON request bodies are limited to 1 MiB, and batch deletion accepts at most 500 IDs. Collection endpoints use the paginated routes; the legacy all-record `time-jobs`, `cron-jobs`, and `cron-job-occurrences/{cronJobId}` routes are not exposed.

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
        // Or opt out explicitly with dashboard.WithNoAuth() — isolated development environments only.
    });
```

Auth detection is automatic: explicit `WithNoAuth()` → public; basic auth → username/password login UI; API key → bearer token; host auth → delegates to the host's authentication middleware.

## Dependencies

- `Headless.Jobs.Abstractions`
- `Headless.Jobs.Core`
- `Headless.Dashboard.Authentication` (shared with `Headless.Messaging.Dashboard`)

## Side Effects

- Mounts dashboard HTTP API and SignalR hub under `SetBasePath` path via `IStartupFilter` (no explicit `app.Use…` call needed).
- Subscribes to `Headless.Coordination` membership events for live-node push updates.
- Serves embedded frontend SPA assets; requires Node 22 on `PATH` when building from source.
- Exposes mutating operational endpoints; configure authentication and CORS before exposing the dashboard outside an isolated development environment.
