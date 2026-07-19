# Headless.Messaging.Dashboard

Web-based dashboard for monitoring and managing distributed messaging infrastructure.

## Problem Solved

Provides real-time visibility into message processing, failures, retries, and system health through an embedded web UI for operations and troubleshooting.

## Key Features

- **Real-Time Monitoring**: Live message throughput and latency metrics
- **Message Explorer**: Search, filter, and inspect messages
- **Failure Management**: View and retry failed messages
- **Node Discovery**: Multi-instance cluster visibility through async `INodeDiscoveryProvider` operations with optional trailing cancellation tokens; implementations propagate caller-requested cancellation instead of converting it to an empty or not-found result
- **Performance Metrics**: Consumer processing stats and bottlenecks
- **5-Mode Auth**: None, Basic, API Key, Host, Custom (shared with Jobs Dashboard)

## Design Notes

The dashboard exposes operational endpoints for inspecting, retrying, re-executing, and deleting message records. Treat `WithNoAuth()` as development-only unless the dashboard is isolated behind trusted network controls. Production deployments should use `WithHostAuthentication(...)`, `WithBasicAuth(...)`, `WithApiKey(...)`, or `WithCustomAuth(...)`, and should set an explicit CORS policy before exposing the dashboard cross-origin.

## Installation

```bash
dotnet add package Headless.Messaging.Dashboard
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.ForMessagesFromAssemblyContaining<Program>();
    options.UsePostgreSql("connection_string");
    options.UseRabbitMq(config);

    options.UseDashboard(dashboard =>
    {
        dashboard.WithBasicAuth("admin", "secret123");
    });
});

// Access dashboard at: http://localhost:5000/messaging
```

## Authentication Modes

An authentication mode must be chosen explicitly: if none of the `WithXxx` auth methods below (including `WithNoAuth()`) is called, the host **fails to start**. This is intentional — the dashboard exposes operational message actions and never ships publicly by omission.

### No Authentication (Dev/Testing Only)

```csharp
options.UseDashboard(dashboard =>
{
    dashboard.WithNoAuth();
});
```

### Basic Authentication

```csharp
options.UseDashboard(dashboard =>
{
    dashboard.WithBasicAuth("admin", "secret123");
});
```

### API Key Authentication

```csharp
options.UseDashboard(dashboard =>
{
    dashboard.WithApiKey("my-secret-api-key");
});
```

### Use Host Application's Authentication

```csharp
options.UseDashboard(dashboard =>
{
    dashboard.WithHostAuthentication();
});
```

### Use Host Authentication with Custom Policy

```csharp
options.UseDashboard(dashboard =>
{
    dashboard.WithHostAuthentication("DashboardPolicy");
});
```

### Custom Authentication

```csharp
options.UseDashboard(dashboard =>
{
    dashboard.WithCustomAuth((token, services) => ValidateToken(token, services));
});
```

## Fluent API Methods

- `WithNoAuth()` - Explicitly opt out of authentication (development or trusted-network use only)
- `WithBasicAuth(username, password)` - Enable username/password authentication
- `WithApiKey(apiKey)` - Enable API key authentication
- `WithHostAuthentication(policy?)` - Use your app's existing auth with optional policy
- `WithCustomAuth(validator)` - Custom authentication with validation function
- `WithSessionTimeout(minutes)` - Set session timeout (default: 60 minutes)
- `SetBasePath(path)` - Set dashboard URL path (default: `/messaging`)
- `SetStatsPollingInterval(ms)` - Stats polling interval (default: 2000ms)
- `SetCorsOrigins(origins)` - Allow specific cross-origin origins (credentialed); use when the SPA is served cross-origin
- `SetCorsPolicy(policy)` - Configure a custom CORS policy

## Configuration

| Method | Default | Description |
|--------|---------|-------------|
| `SetBasePath` | `/messaging` | URL path for the dashboard |
| `SetStatsPollingInterval` | `2000` | Stats endpoint polling interval (ms) |
| `WithNoAuth` | (no default — auth is required) | Explicitly opt out of authentication; development or trusted-network use only |
| `SetCorsPolicy` / `SetCorsOrigins` | `null` (same-origin only) | CORS policy for cross-origin requests |
| `WithSessionTimeout` | `60` | Session timeout in minutes |

## Dependencies

- `Headless.Messaging.Core`
- `Headless.Dashboard.Authentication` (shared auth with Jobs Dashboard)
- `Consul` (node discovery)
- `Microsoft.AspNetCore.App` (framework reference)

## Side Effects

- Mounts the embedded web UI and monitoring API through an `IStartupFilter` — no explicit middleware call is required
- Exposes a web endpoint at the configured path (default: `/messaging`)
- Periodically polls message storage for statistics
- Authentication must be configured explicitly (an auth mode, or an explicit `WithNoAuth()` opt-out) or the host fails to start; no CORS policy is applied by default (same-origin only)
