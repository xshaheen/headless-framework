---
title: "Merge Jobs + Messaging Dashboards into Unified Core+Plugins Architecture"
type: refactor
date: 2026-03-22
status: active
origin: docs/brainstorms/2026-03-22-unified-dashboard-requirements.md
---

# Merge Jobs + Messaging Dashboards into Unified Core+Plugins Architecture

## Overview

Replace `Headless.Jobs.Dashboard` and `Headless.Messaging.Dashboard` with a core+plugins architecture: `Headless.Dashboard` (core SPA, auth, module discovery), `Headless.Dashboard.Jobs` (jobs endpoints), `Headless.Dashboard.Messaging` (messaging endpoints). The single Vue SPA lives in core and adapts at runtime based on which plugins are registered.

## Problem Statement / Motivation

(see origin: docs/brainstorms/2026-03-22-unified-dashboard-requirements.md)

- **Unified UX**: Consumers want one dashboard, not two separate UIs at different base paths
- **Reduce duplication**: ~60% of frontend code is duplicated; auth, layout, stores, build pipeline are copy-pasted
- **Simpler adoption**: One `AddHeadlessDashboard()` call with shared auth, not two separate configurations

## Proposed Solution

### Architecture

```
Headless.Dashboard                    (core: SPA, auth, module discovery, shared middleware)
├── Headless.Dashboard.Jobs           (jobs API endpoints, module descriptor)
├── Headless.Dashboard.Messaging      (messaging API endpoints, module descriptor)
└── Headless.Dashboard.Messaging.K8s  (K8s node discovery, unchanged scope)
```

### Package Dependencies

```
Headless.Dashboard
├── Headless.Checks
├── Microsoft.AspNetCore.App
└── (absorbs Headless.Dashboard.Authentication)

Headless.Dashboard.Jobs
├── Headless.Dashboard               ← new dependency
├── Headless.Jobs.Abstractions
└── Headless.Jobs.Core

Headless.Dashboard.Messaging
├── Headless.Dashboard               ← new dependency
├── Headless.Messaging.Core
└── Consul (optional)

Headless.Dashboard.Messaging.K8s
├── Headless.Dashboard.Messaging     ← updated from Headless.Messaging.Dashboard
└── KubernetesClient
```

### Consumer API

```csharp
// 1. Core dashboard — auth + base path configured here
builder.Services.AddHeadlessDashboard(d => {
    d.WithBasicAuth("admin", "pw");
    d.SetBasePath("/dashboard");
});

// 2. Plugins attach — no auth config
builder.Services.AddHeadlessJobs(o => o.UseDashboard());
builder.Services.AddHeadlessMessaging(o => o.UseDashboard());

// Jobs-only consumer — works fine, messaging sections hidden
builder.Services.AddHeadlessDashboard(d => d.WithApiKey("key"));
builder.Services.AddHeadlessJobs(o => o.UseDashboard());
```

## Technical Approach

### 1. Module Descriptor Interface (`IDashboardModule`)

The load-bearing abstraction. Each plugin implements this and registers it in DI.

```csharp
// Headless.Dashboard/IDashboardModule.cs
public interface IDashboardModule
{
    /// Unique module identifier (e.g., "jobs", "messaging")
    string Id { get; }

    /// Display order in sidebar (lower = higher)
    int Order { get; }

    /// Register module-specific API endpoints (receives read-only options)
    void MapEndpoints(IEndpointRouteBuilder endpoints, DashboardOptions options);

    /// Register module-specific DI services (background services, repositories, etc.)
    /// Called during AddHeadlessDashboard() service registration.
    void AddServices(IServiceCollection services);

    /// Module metadata for frontend /api/modules endpoint
    DashboardModuleManifest GetManifest();
}

public sealed class DashboardModuleManifest
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Icon { get; init; }
    public required int Order { get; init; }
    public required IReadOnlyList<DashboardModuleSection> Sections { get; init; }

    /// Plugin-specific config values injected into window.DashboardConfig.modules[id]
    public IDictionary<string, object>? ClientConfig { get; init; }
}

public sealed class DashboardModuleSection
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Icon { get; init; }
    public required string Route { get; init; }
}
```

### 2. API Route Namespacing

Plugin endpoints are namespaced under `/api/{moduleId}/...` to avoid collisions.

| Current | Unified |
|---------|---------|
| `/api/time-jobs` | `/api/jobs/time-jobs` |
| `/api/cron-jobs` | `/api/jobs/cron-jobs` |
| `/api/job-host` | `/api/jobs/host` |
| `/job-notification-hub` | `/jobs/hub` |
| `/api/published/{status}` | `/api/messaging/published/{status}` |
| `/api/received/{status}` | `/api/messaging/received/{status}` |
| `/api/metrics-realtime` | `/api/messaging/metrics-realtime` |
| `/api/nodes` | `/api/messaging/nodes` |
| `/api/subscriber` | `/api/messaging/subscriber` |

Auth endpoints move to core (deduplicated):
- `/api/auth/info` — core owns this
- `/api/auth/validate` — core owns this

### 3. DI Registration & Validation

```csharp
// Headless.Dashboard/DependencyInjection/ServiceCollectionExtensions.cs
public static IServiceCollection AddHeadlessDashboard(
    this IServiceCollection services,
    Action<DashboardOptionsBuilder> configure)
{
    var builder = new DashboardOptionsBuilder();
    configure(builder);
    builder.Validate(); // auth validation

    // Marker service for plugin validation
    services.TryAddSingleton<DashboardCoreMarker>();
    services.AddSingleton(builder);
    services.AddSingleton(builder.Auth);
    services.AddScoped<IAuthService, AuthService>();
    services.AddRouting();
    services.AddAuthorization();
    services.AddCors();

    // IStartupFilter that builds the unified pipeline
    services.AddSingleton<IStartupFilter, DashboardStartupFilter>();

    return services;
}
```

Plugin registration (Jobs example):

```csharp
// Headless.Dashboard.Jobs/DependencyInjection/JobsDashboardExtensions.cs
public static JobsOptionsBuilder AddDashboard(this JobsOptionsBuilder builder)
{
    builder.DashboardServiceAction = services =>
    {
        // Register module descriptor
        services.AddSingleton<IDashboardModule, JobsDashboardModule>();
        // Jobs-specific dashboard services
        services.AddScoped<JobsDashboardRepository>();
    };
    return builder;
}
```

Validation via `IStartupFilter`:

```csharp
internal sealed class DashboardStartupFilter(
    DashboardOptionsBuilder options,
    IEnumerable<IDashboardModule> modules) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            next(app);

            // Warn if no modules registered (valid state, shows empty dashboard)
            if (!modules.Any())
            {
                // Log warning: "Dashboard registered but no modules found"
            }

            app.UseDashboard(options, modules);
        };
    }
}
```

Plugin-side validation (in Jobs' `IStartupFilter` or registration):

```csharp
// Validate core is registered
if (!services.Any(s => s.ServiceType == typeof(DashboardCoreMarker)))
{
    throw new InvalidOperationException(
        "AddHeadlessDashboard() must be called before AddDashboard(). " +
        "Register the core dashboard first.");
}
```

**Edge cases**:
- `AddHeadlessDashboard()` called twice → idempotent (TryAddSingleton), second call logs warning
- Plugin `AddDashboard()` called twice → idempotent (TryAddSingleton for the module)
- Core with zero plugins → valid; shows empty dashboard with "No modules registered" message
- Plugin without core → throws `InvalidOperationException` at registration time

### 4. Unified Middleware Pipeline

Single `app.Map(basePath)` branch owned by the core:

```csharp
internal static void UseDashboard(
    this IApplicationBuilder app,
    DashboardOptionsBuilder options,
    IEnumerable<IDashboardModule> modules)
{
    var basePath = DashboardSpaHelper.NormalizeBasePath(options.BasePath);

    app.Map(basePath, dashboardApp =>
    {
        // 1. Pre-dashboard middleware hook
        options.PreDashboardMiddleware?.Invoke(dashboardApp);

        // 2. Static files (embedded SPA assets — before auth)
        dashboardApp.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new EmbeddedFileProvider(
                typeof(DashboardOptionsBuilder).Assembly,
                "Headless.Dashboard.wwwroot.dist"),
            RequestPath = ""
        });

        // 3. Routing + CORS
        dashboardApp.UseRouting();
        dashboardApp.UseCors(options.CorsPolicy);

        // 4. Auth middleware (if enabled)
        if (options.Auth.IsEnabled)
            dashboardApp.UseMiddleware<AuthMiddleware>();

        dashboardApp.UseAuthorization();

        // 5. Custom middleware hook
        options.CustomMiddleware?.Invoke(dashboardApp);

        // 6. Endpoints
        dashboardApp.UseEndpoints(endpoints =>
        {
            // Core endpoints: /api/auth/*, /api/modules, /api/health
            MapCoreEndpoints(endpoints, options, modules);

            // Plugin endpoints: each module maps its own group
            foreach (var module in modules.OrderBy(m => m.Order))
            {
                module.MapEndpoints(endpoints, options);
            }
        });

        // 7. Post-dashboard middleware hook
        options.PostDashboardMiddleware?.Invoke(dashboardApp);

        // 8. SPA fallback (404 → index.html with injected config)
        dashboardApp.UseSpaFallback(options, modules);
    });
}
```

**GatewayProxy scoping**: See Section 6 — applied only on the messaging endpoint group, never Jobs.

**CORS**: Applied once at pipeline level by the core. Plugins do **not** add `.RequireCors()` on their groups — the pipeline-level `UseCors()` covers all endpoints under the dashboard base path.

### 5. Auth & Session Strategy

Auth is **header-based only** — no cookies for auth. Both dashboards store credentials in localStorage and send `Authorization` headers per-request. The unified dashboard:

- Renames localStorage keys from `jobs_*` / `messaging_*` to `dashboard_*`
- Single `AuthService` reads `window.DashboardConfig.auth`
- SignalR token generation via `getAccessToken()` stays (Jobs uses it for WebSocket auth)
- Users re-login once after upgrade (key names change) — acceptable for breaking change

### 6. Multi-Node Gateway Proxy (Messaging-Only)

The GatewayProxy is **messaging-specific** and uses cookies (`messaging.node`, `messaging.node.ns`) to track which remote node the user is viewing — this is **not** auth, it's node routing.

**Scoping in unified pipeline**: The `GatewayProxyEndpointFilter` is applied **only** on the messaging endpoint group. Jobs endpoints are never proxied.

```csharp
// In MessagingDashboardModule.MapEndpoints():
var group = endpoints
    .MapGroup("/api/messaging")
    .WithTags("Messaging Dashboard")
    .RequireCors(...)
    .AddEndpointFilter<GatewayProxyEndpointFilter>(); // messaging-only proxy

// Jobs group has no proxy filter
var jobsGroup = endpoints
    .MapGroup("/api/jobs")
    .WithTags("Jobs Dashboard")
    .RequireCors(...);
```

Cookie names stay unchanged (`messaging.node`, `messaging.node.ns`). The `GatewayProxyAgent`, `IRequestMapper`, and `INodeDiscoveryProvider` all live in `Headless.Dashboard.Messaging`. The K8s extension (`Headless.Dashboard.Messaging.K8s`) continues to extend node discovery.

### 7. Plugin-Owned Background Services

Plugins register their own background services via their DI extension — the core has no knowledge of them.

```csharp
// Messaging plugin registers its own services:
services.AddSingleton<IDashboardModule, MessagingDashboardModule>();
services.AddSingleton<MessagingMetricsEventListener>();
services.AddHostedService(sp => sp.GetRequiredService<MessagingMetricsEventListener>());
services.AddSingleton<CircularBuffer<MetricsSample>>();
services.AddHttpClient<GatewayProxyAgent>();

// Jobs plugin registers its own services:
services.AddSingleton<IDashboardModule, JobsDashboardModule>();
services.AddScoped<JobsDashboardRepository>();
services.AddSignalR(); // if not already added
```

**Messaging-specific infrastructure that stays in the messaging plugin:**

| Component | Purpose |
|-----------|---------|
| `MessagingMetricsEventListener` | EventSource listener sampling real-time throughput/latency (1s intervals, 300s ring buffer) |
| `CircularBuffer<T>` | Fixed-capacity ring buffer for metrics history |
| `GatewayProxyAgent` | HTTP forwarder for multi-node request proxying |
| `GatewayProxyEndpointFilter` | Endpoint filter that intercepts proxied requests |
| `IRequestMapper` / `RequestMapper` | Maps incoming requests to outgoing proxy requests |
| `INodeDiscoveryProvider` | Interface for node discovery (Consul, K8s implementations) |
| `HtmlHelper` | Renders C# method signatures as highlighted HTML for subscriber display |

**Jobs-specific infrastructure that stays in the jobs plugin:**

| Component | Purpose |
|-----------|---------|
| `JobsNotificationHub` | SignalR hub for real-time job status updates |
| `JobsNotificationHubSender` | Sends notifications to connected hub clients |
| `JobsDashboardRepository` | Data access for job queries and dashboard stats |
| `JsonExampleGenerator` | Generates example JSON payloads for job parameters |

### 8. Frontend Config Injection

Unified config shape injected into `index.html`:

```js
window.DashboardConfig = {
  basePath: "/dashboard",
  auth: { mode: "basic", enabled: true, sessionTimeout: 3600 },
  modules: {
    jobs: { backendDomain: null },
    messaging: { statsPollingInterval: 2000 }
  }
}
```

Built from module manifests:

```csharp
private static string BuildClientConfig(
    DashboardOptionsBuilder options,
    IEnumerable<IDashboardModule> modules)
{
    var config = new
    {
        basePath = options.BasePath,
        auth = new { options.Auth.Mode, options.Auth.IsEnabled, options.Auth.SessionTimeout },
        modules = modules.ToDictionary(
            m => m.Id,
            m => m.GetManifest().ClientConfig ?? new Dictionary<string, object>())
    };
    return JsonSerializer.Serialize(config);
}
```

### 9. `/api/modules` Endpoint

```csharp
// Core endpoint
endpoints.MapGet("/api/modules", (IEnumerable<IDashboardModule> modules) =>
{
    var manifests = modules
        .OrderBy(m => m.Order)
        .Select(m => m.GetManifest());
    return Results.Ok(manifests);
}).WithName("GetModules").AllowAnonymous(); // After auth — SPA needs this at boot
```

**Failure handling**: The SPA catches fetch errors on boot and shows an error banner with a retry button. Since the SPA has all module code baked in, no functionality is lost — just the module discovery metadata.

### 10. SignalR Hub Under Unified Pipeline

The Jobs SignalR hub maps at `/jobs/hub` relative to the dashboard base path (e.g., `/dashboard/jobs/hub`). Auth is handled manually in `OnConnectedAsync` via `IAuthService` — same pattern as today, but `IAuthService` is now registered by the core.

```csharp
// In JobsDashboardModule.MapEndpoints:
endpoints.MapHub<JobsNotificationHub>("/jobs/hub").AllowAnonymous();
```

### 11. DashboardOptionsBuilder (Unified Core Builder)

```csharp
public sealed class DashboardOptionsBuilder
{
    public string BasePath { get; set; } = "/dashboard";
    public AuthConfig Auth { get; set; } = new();
    public Action<CorsPolicyBuilder>? CorsPolicy { get; set; }
    public Action<IApplicationBuilder>? PreDashboardMiddleware { get; set; }
    public Action<IApplicationBuilder>? CustomMiddleware { get; set; }
    public Action<IApplicationBuilder>? PostDashboardMiddleware { get; set; }

    // Auth fluent API (moved from both builders)
    public DashboardOptionsBuilder WithNoAuth() { ... }
    public DashboardOptionsBuilder WithBasicAuth(string username, string password) { ... }
    public DashboardOptionsBuilder WithApiKey(string apiKey) { ... }
    public DashboardOptionsBuilder WithHostAuthentication(string? policy = null) { ... }
    public DashboardOptionsBuilder WithCustomAuth(Func<string, IServiceProvider, bool> validate) { ... }
    public DashboardOptionsBuilder WithSessionTimeout(TimeSpan timeout) { ... }
    public DashboardOptionsBuilder SetBasePath(string basePath) { ... }

    internal void Validate() => Auth.Validate();
}
```

**Plugin-specific options** (Jobs' `BackendDomain`, `DashboardJsonOptions`; Messaging's `StatsPollingInterval`) stay on the plugin's own config, registered as singletons in DI. Plugins resolve their own options inside `MapEndpoints` via `IEndpointRouteBuilder.ServiceProvider`. Exposed to the frontend via `DashboardModuleManifest.ClientConfig`.

**`DashboardOptions`** (read-only, built from builder):

```csharp
public sealed class DashboardOptions
{
    public required string BasePath { get; init; }
    public required AuthConfig Auth { get; init; }
    public required Action<CorsPolicyBuilder>? CorsPolicy { get; init; }
}
```

`DashboardOptionsBuilder.Build()` produces the immutable `DashboardOptions` during startup. Plugins receive the read-only version, never the builder.

## Implementation Phases

### Phase 1: Core Package (`Headless.Dashboard`)

**New files to create:**

| File | Purpose |
|------|---------|
| `src/Headless.Dashboard/Headless.Dashboard.csproj` | Package project |
| `src/Headless.Dashboard/IDashboardModule.cs` | Module interface + manifest types |
| `src/Headless.Dashboard/DashboardOptionsBuilder.cs` | Unified builder (absorbs auth fluent API) |
| `src/Headless.Dashboard/DashboardCoreMarker.cs` | Marker service for validation |
| `src/Headless.Dashboard/DependencyInjection/ServiceCollectionExtensions.cs` | `AddHeadlessDashboard()` |
| `src/Headless.Dashboard/Setup.cs` | `UseDashboard()` pipeline + `DashboardStartupFilter` |
| `src/Headless.Dashboard/Endpoints/CoreEndpoints.cs` | `/api/auth/*`, `/api/modules`, `/api/health` |
| `src/Headless.Dashboard/SpaFallbackMiddleware.cs` | 404→index.html with config injection |

**Moved from `Headless.Dashboard.Authentication`:**

| Source | Destination |
|--------|-------------|
| `AuthConfig.cs` | `src/Headless.Dashboard/Auth/AuthConfig.cs` |
| `AuthMode.cs` | `src/Headless.Dashboard/Auth/AuthMode.cs` |
| `AuthService.cs` | `src/Headless.Dashboard/Auth/AuthService.cs` |
| `IAuthService.cs` | `src/Headless.Dashboard/Auth/IAuthService.cs` |
| `AuthMiddleware.cs` | `src/Headless.Dashboard/Auth/AuthMiddleware.cs` |
| `AuthResult.cs` | `src/Headless.Dashboard/Auth/AuthResult.cs` |
| `DashboardSpaHelper.cs` | `src/Headless.Dashboard/DashboardSpaHelper.cs` |

**After moving**: Delete `src/Headless.Dashboard.Authentication/` project entirely.

### Phase 2: Plugin Packages

#### `Headless.Dashboard.Jobs`

**New files:**

| File | Purpose |
|------|---------|
| `src/Headless.Dashboard.Jobs/Headless.Dashboard.Jobs.csproj` | Package project, references `Headless.Dashboard` + `Headless.Jobs.Core` |
| `src/Headless.Dashboard.Jobs/JobsDashboardModule.cs` | `IDashboardModule` implementation |
| `src/Headless.Dashboard.Jobs/JobsDashboardOptions.cs` | Jobs-specific options (`BackendDomain`, `DashboardJsonOptions`) |
| `src/Headless.Dashboard.Jobs/DependencyInjection/JobsDashboardExtensions.cs` | `AddDashboard()` extension on `JobsOptionsBuilder` |

**Moved from `Headless.Jobs.Dashboard`:**

| Source | Destination |
|--------|-------------|
| `Endpoints/DashboardEndpoints.cs` | `src/Headless.Dashboard.Jobs/Endpoints/JobsDashboardEndpoints.cs` (remove auth endpoints, namespace under `/api/jobs/`) |
| `Infrastructure/Dashboard/JobsDashboardRepository.cs` | `src/Headless.Dashboard.Jobs/Infrastructure/JobsDashboardRepository.cs` |
| `Infrastructure/JsonExampleGenerator.cs` | `src/Headless.Dashboard.Jobs/Infrastructure/JsonExampleGenerator.cs` |
| `Infrastructure/StringToByteArrayConverter.cs` | `src/Headless.Dashboard.Jobs/Infrastructure/StringToByteArrayConverter.cs` |
| `Hubs/JobsNotificationHub.cs` | `src/Headless.Dashboard.Jobs/Hubs/JobsNotificationHub.cs` |
| `Hubs/JobsNotificationHubSender.cs` | `src/Headless.Dashboard.Jobs/Hubs/JobsNotificationHubSender.cs` |

**After moving**: Delete `src/Headless.Jobs.Dashboard/` project entirely.

#### `Headless.Dashboard.Messaging`

**New files:**

| File | Purpose |
|------|---------|
| `src/Headless.Dashboard.Messaging/Headless.Dashboard.Messaging.csproj` | Package project, references `Headless.Dashboard` + `Headless.Messaging.Core` |
| `src/Headless.Dashboard.Messaging/MessagingDashboardModule.cs` | `IDashboardModule` implementation |
| `src/Headless.Dashboard.Messaging/MessagingDashboardOptions.cs` | Messaging-specific options (`StatsPollingInterval`) |
| `src/Headless.Dashboard.Messaging/DependencyInjection/MessagingDashboardExtensions.cs` | `UseDashboard()` extension on `MessagingOptions` |

**Moved from `Headless.Messaging.Dashboard`:**

| Source | Destination |
|--------|-------------|
| `Endpoints/MessagingDashboardEndpoints.cs` | `src/Headless.Dashboard.Messaging/Endpoints/MessagingDashboardEndpoints.cs` (remove auth endpoints, namespace under `/api/messaging/`) |
| `CircularBuffer.cs` | `src/Headless.Dashboard.Messaging/CircularBuffer.cs` |
| `MessagingMetricsEventListener.cs` | `src/Headless.Dashboard.Messaging/MessagingMetricsEventListener.cs` |
| `GatewayProxy/*` | `src/Headless.Dashboard.Messaging/GatewayProxy/*` |
| `NodeDiscovery/*` | `src/Headless.Dashboard.Messaging/NodeDiscovery/*` |

**After moving**: Delete `src/Headless.Messaging.Dashboard/` project entirely.

#### `Headless.Dashboard.Messaging.K8s`

- Rename from `Headless.Messaging.Dashboard.K8s`
- Update `ProjectReference` from `Headless.Messaging.Dashboard` → `Headless.Dashboard.Messaging`
- Minimal code changes — scope unchanged

### Phase 3: Frontend Merge

**Location**: `src/Headless.Dashboard/wwwroot/`

#### 3a. Scaffold Unified SPA

Create the merged project structure:

```
src/Headless.Dashboard/wwwroot/
├── src/
│   ├── core/
│   │   ├── components/
│   │   │   ├── AuthHeader.vue
│   │   │   ├── ConfirmDialog.vue
│   │   │   ├── GlobalAlerts.vue
│   │   │   ├── PaginationFooter.vue
│   │   │   └── TableSkeleton.vue
│   │   ├── composables/
│   │   │   ├── useAlert.ts
│   │   │   ├── useDialog.ts
│   │   │   └── usePagination.ts
│   │   ├── layout/
│   │   │   └── DashboardLayout.vue       ← new: sidebar with collapsible module sections
│   │   ├── services/
│   │   │   ├── auth.ts                   ← parameterized by window.DashboardConfig
│   │   │   └── http.ts                   ← parameterized by window.DashboardConfig
│   │   ├── stores/
│   │   │   ├── authStore.ts
│   │   │   ├── alertStore.ts
│   │   │   └── modulesStore.ts           ← new: fetches /api/modules, manages active modules
│   │   ├── types/
│   │   │   └── dashboard-config.d.ts     ← window.DashboardConfig type
│   │   └── utilities/
│   │       ├── dateTimeParser.ts
│   │       └── pathResolver.ts
│   ├── modules/
│   │   ├── jobs/
│   │   │   ├── views/
│   │   │   │   ├── Dashboard.vue
│   │   │   │   ├── CronJob.vue
│   │   │   │   └── TimeJob.vue
│   │   │   ├── components/
│   │   │   │   ├── ChainJobsModal.vue
│   │   │   │   ├── CRUDCronJobDialog.vue
│   │   │   │   ├── CronOccurrenceDialog.vue
│   │   │   │   ├── CRUDTimeJobDialogComponent.vue
│   │   │   │   ├── JobRequestDialog.vue
│   │   │   │   ├── ErrorAlert.vue
│   │   │   │   └── TimeJobCharts.vue
│   │   │   ├── stores/
│   │   │   │   ├── connectionStore.ts
│   │   │   │   ├── dashboardStore.ts
│   │   │   │   ├── timeZoneStore.ts
│   │   │   │   └── functionNames.ts
│   │   │   ├── services/
│   │   │   │   ├── cronJobService.ts
│   │   │   │   ├── cronJobOccurrenceService.ts
│   │   │   │   ├── jobsService.ts
│   │   │   │   ├── timeJobService.ts
│   │   │   │   └── signalr.ts
│   │   │   ├── composables/
│   │   │   │   └── useCustomForm.ts
│   │   │   └── routes.ts
│   │   └── messaging/
│   │       ├── views/
│   │       │   ├── Dashboard.vue
│   │       │   ├── Published.vue
│   │       │   ├── Received.vue
│   │       │   ├── Subscribers.vue
│   │       │   └── Nodes.vue
│   │       ├── components/
│   │       │   ├── MessageDetailDialog.vue
│   │       │   └── MessagingCharts.vue
│   │       ├── stores/
│   │       │   └── messagingStore.ts
│   │       └── routes.ts
│   ├── App.vue
│   ├── main.ts
│   └── router/
│       └── index.ts
├── package.json                          ← unified deps
├── vite.config.ts
├── tsconfig.json
├── tailwind.config.js
└── postcss.config.js
```

#### 3b. Core Frontend Changes

**`main.ts`** — Bootstrap flow:

```ts
// 1. Read window.DashboardConfig (injected by server)
// 2. Initialize auth service with config
// 3. Create Pinia stores
// 4. Create router with core routes (login, overview)
// 5. Fetch /api/modules → register active module routes dynamically
// 6. Mount app
```

**`router/index.ts`** — Conditional route registration:

```ts
import { jobsRoutes } from '@/modules/jobs/routes'
import { messagingRoutes } from '@/modules/messaging/routes'

const moduleRouteMap: Record<string, RouteRecordRaw[]> = {
  jobs: jobsRoutes,
  messaging: messagingRoutes,
}

export async function registerModuleRoutes(router: Router, activeModules: string[]) {
  for (const id of activeModules) {
    const routes = moduleRouteMap[id]
    if (routes) {
      routes.forEach(route => router.addRoute(route))
    }
  }
}
```

**`modulesStore.ts`** — Module state:

```ts
export const useModulesStore = defineStore('modules', () => {
  const modules = ref<DashboardModuleManifest[]>([])
  const error = ref<string | null>(null)
  const isLoading = ref(true)

  async function fetchModules() {
    try {
      const response = await http.get<DashboardModuleManifest[]>('/api/modules')
      modules.value = response
    } catch (e) {
      error.value = 'Failed to load dashboard modules. Retrying...'
      // Show error banner with retry button
    } finally {
      isLoading.value = false
    }
  }

  const activeModuleIds = computed(() => modules.value.map(m => m.id))

  return { modules, error, isLoading, fetchModules, activeModuleIds }
})
```

**`DashboardLayout.vue`** — Sidebar with collapsible sections:

- Reads `modulesStore.modules` for sections
- Each section has icon, label, sub-items from manifest
- Collapsible via Vuetify `v-list-group`
- Hides sections for inactive modules
- Overview section always visible

#### 3c. Module Route Files

**`modules/jobs/routes.ts`**:
```ts
export const jobsRoutes: RouteRecordRaw[] = [
  {
    path: '/jobs',
    children: [
      { path: '', component: () => import('./views/Dashboard.vue') },
      { path: 'time-jobs', component: () => import('./views/TimeJob.vue') },
      { path: 'cron-jobs', component: () => import('./views/CronJob.vue') },
    ],
  },
]
```

**`modules/messaging/routes.ts`**:
```ts
export const messagingRoutes: RouteRecordRaw[] = [
  {
    path: '/messaging',
    children: [
      { path: '', component: () => import('./views/Dashboard.vue') },
      { path: 'published', component: () => import('./views/Published.vue') },
      { path: 'received', component: () => import('./views/Received.vue') },
      { path: 'subscribers', component: () => import('./views/Subscribers.vue') },
      { path: 'nodes', component: () => import('./views/Nodes.vue') },
    ],
  },
]
```

#### 3d. Service Parameterization

**`core/services/auth.ts`** — Unified auth service:
- Replace `window.JobsConfig` / `window.MessagingConfig` with `window.DashboardConfig`
- LocalStorage keys become `dashboard_basic_auth`, `dashboard_api_key`, `dashboard_host_access_key`
- SignalR token generation stays (used by Jobs module)

**`core/services/http.ts`** — Unified HTTP client:
- Base URL from `window.DashboardConfig.basePath`
- All module API calls go through this (e.g., `/api/jobs/time-jobs`, `/api/messaging/published/Succeeded`)

#### 3e. Frontend API Path Updates

All module views/services update their API paths to include the module namespace:

| Module | Current | Updated |
|--------|---------|---------|
| Jobs | `get('/api/time-jobs')` | `get('/api/jobs/time-jobs')` |
| Jobs | `post('/api/cron-jobs')` | `post('/api/jobs/cron-jobs')` |
| Jobs | `hub('/job-notification-hub')` | `hub('/jobs/hub')` |
| Messaging | `get('/api/published/Succeeded')` | `get('/api/messaging/published/Succeeded')` |
| Messaging | `get('/api/metrics-realtime')` | `get('/api/messaging/metrics-realtime')` |

### Phase 4: Tests

#### New Test Projects

| Project | Tests |
|---------|-------|
| `Headless.Dashboard.Tests.Unit` | Core endpoints, auth middleware, module discovery, config injection, builder validation |
| `Headless.Dashboard.Jobs.Tests.Unit` | Jobs module descriptor, endpoint registration, hub auth |
| `Headless.Dashboard.Messaging.Tests.Unit` | Messaging module descriptor, endpoint registration, gateway proxy scoping |

**Migrated from:**
- `Headless.Dashboard.Authentication.Tests.Unit` → `Headless.Dashboard.Tests.Unit`
- `Headless.Messaging.Dashboard.Tests.Unit` → `Headless.Dashboard.Messaging.Tests.Unit`
- `Headless.Messaging.Dashboard.K8s.Tests.Unit` → stays, updated references

**Key test scenarios:**
- Core without plugins → empty dashboard, no crash
- Plugin without core → `InvalidOperationException`
- Duplicate registration → idempotent
- `/api/modules` returns correct manifests for registered modules
- Auth middleware protects all plugin endpoints
- GatewayProxy filter only applies to messaging endpoints
- SignalR hub auth works via shared `IAuthService`

### Phase 5: Demo Apps & Cleanup

**Update demo apps:**

| Current | Updated |
|---------|---------|
| `Headless.Jobs.Dashboard.Jwt.Demo` | Update to use `AddHeadlessDashboard()` + `o.AddDashboard()` |
| `Headless.Messaging.Dashboard.Jwt.Demo` | Update to use `AddHeadlessDashboard()` + `o.UseDashboard()` |
| `Headless.Messaging.Dashboard.Auth.Demo` | Update to new package references |

Consider adding a combined demo showing both modules in one dashboard.

**Delete old packages:**
- `src/Headless.Jobs.Dashboard/`
- `src/Headless.Messaging.Dashboard/`
- `src/Headless.Dashboard.Authentication/`
- `src/Headless.Messaging.Dashboard.K8s/` (renamed, not deleted)
- Old test projects for the above

**Update:**
- `Directory.Packages.props` — remove old package entries, add new
- Solution file — remove old projects, add new
- Any CI/CD that references old package names

## System-Wide Impact

### Interaction Graph

- `AddHeadlessDashboard()` → registers core services + `IStartupFilter`
- Plugin `AddDashboard()` → registers `IDashboardModule` singleton
- `DashboardStartupFilter.Configure()` → resolves all `IDashboardModule` → calls `app.UseDashboard()`
- `app.UseDashboard()` → `app.Map(basePath)` → static files → auth → routing → core endpoints + plugin endpoints → SPA fallback
- SPA boot → `GET /api/modules` → conditional route registration → sidebar rendering

### Error Propagation

- Plugin without core → `InvalidOperationException` at DI registration (fail fast)
- `/api/modules` failure → frontend error banner with retry (graceful degradation)
- Auth failure → 401 from `AuthMiddleware` → frontend redirects to login
- SignalR auth failure → `OnConnectedAsync` returns error → frontend shows disconnected state

### State Lifecycle Risks

- **Singleton `DashboardOptionsBuilder`**: Created during DI, immutable after. No race conditions.
- **Module descriptor singletons**: Registered once, read-only at runtime. Safe.
- **Frontend localStorage**: Keys change from `jobs_*`/`messaging_*` to `dashboard_*`. Consumers' existing sessions will require re-login after upgrade. Acceptable for breaking change.

### API Surface Parity

All existing dashboard endpoints are preserved under new namespaced paths. No functionality removed. Auth endpoints deduplicated into core.

### Integration Test Scenarios

1. Register core + both plugins → verify sidebar shows both sections, all endpoints respond
2. Register core + jobs only → verify messaging section hidden, messaging endpoints return 404
3. Register core + messaging only → verify jobs section hidden, SignalR hub not available
4. Auth mode change → verify all plugin endpoints respect core auth config
5. K8s extension → verify node discovery works through unified pipeline

## Final Acceptance Criteria

### Functional Requirements

- [ ] `AddHeadlessDashboard()` registers core services and SPA
- [ ] `o.AddDashboard()` and `o.UseDashboard()` register plugins without auth config
- [ ] `/api/modules` returns manifests for registered modules only
- [ ] Sidebar shows sections for active modules only
- [ ] All existing Jobs dashboard functionality preserved under `/api/jobs/` namespace
- [ ] All existing Messaging dashboard functionality preserved under `/api/messaging/` namespace
- [ ] SignalR hub works at `/jobs/hub` under unified base path
- [ ] GatewayProxy filter applies only to messaging endpoints
- [ ] All 5 auth modes work in unified dashboard
- [ ] Demo apps updated and functional

### Non-Functional Requirements

- [ ] No performance regression in endpoint response times
- [ ] SPA bundle size within 20% of combined current bundles
- [ ] Auth is configured once, not per-module

### Quality Gates

- [ ] Unit tests for core, both plugins, auth
- [ ] Build succeeds with zero warnings (`dotnet build --no-incremental`)
- [ ] All existing test scenarios migrated to new test projects
- [ ] READMEs updated for all new packages
- [ ] XML docs on all public APIs

## Sources & References

### Origin

- **Origin document:** [docs/brainstorms/2026-03-22-unified-dashboard-requirements.md](docs/brainstorms/2026-03-22-unified-dashboard-requirements.md) — Key decisions: core+plugins architecture (R1), single SPA in core (R2), explicit core-first DI (R5), absorb auth into core (R6), breaking change accepted (R7)

### Internal References

- Auth infrastructure: `src/Headless.Dashboard.Authentication/`
- Jobs dashboard: `src/Headless.Jobs.Dashboard/`
- Messaging dashboard: `src/Headless.Messaging.Dashboard/`
- Jobs builder: `src/Headless.Jobs.Dashboard/DashboardOptionsBuilder.cs`
- Messaging builder: `src/Headless.Messaging.Dashboard/MessagingDashboardOptionsBuilder.cs`
- Plugin extension pattern: `src/Headless.Messaging.Core/Configuration/IMessagesOptionsExtension.cs`
- SPA helper: `src/Headless.Dashboard.Authentication/DashboardSpaHelper.cs`
