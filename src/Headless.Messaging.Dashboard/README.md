# Headless.Messaging.Dashboard

Web-based dashboard for monitoring and managing distributed messaging infrastructure.

## Problem Solved

Provides real-time visibility into message processing, failures, retries, and system health through an embedded web UI for operations and troubleshooting.

## Key Features

- **Real-Time Monitoring**: Live message throughput and latency metrics
- **Message Explorer**: Search, filter, and inspect messages
- **Failure Management**: View and retry failed messages
- **Node Discovery**: Multi-instance cluster visibility
- **Performance Metrics**: Consumer processing stats and bottlenecks

## Installation

```bash
dotnet add package Headless.Messaging.Dashboard
```

## Quick Start

```csharp
builder.Services.AddMessaging(options =>
{
    options.UsePostgreSql("connection_string");
    options.UseRabbitMQ(config);

    options.UseDashboard(dashboard =>
    {
        dashboard.AllowAnonymousExplicit = false;
        dashboard.AuthorizationPolicy = "DashboardPolicy";
    });

    options.SubscribeFromAssemblyContaining<Program>();
});

// Access dashboard at: http://localhost:5000/messaging
```

## Configuration

You **must** explicitly choose an auth mode — either allow anonymous or set a policy. Omitting both throws at startup.

### With Authorization Policy

```csharp
options.UseDashboard(dashboard =>
{
    dashboard.AllowAnonymousExplicit = false;
    dashboard.AuthorizationPolicy = "DashboardPolicy";
});
```

### Anonymous Access (Dev/Testing Only)

```csharp
options.UseDashboard(dashboard =>
{
    dashboard.AllowAnonymousExplicit = true;
});
```

### All Options

| Option | Default | Description |
|--------|---------|-------------|
| `PathMatch` | `/messaging` | URL path for the dashboard |
| `PathBase` | `""` | Base path when behind a reverse proxy |
| `StatsPollingInterval` | `2000` | Stats endpoint polling interval (ms) |
| `AllowAnonymousExplicit` | `false` | Allow unauthenticated access. Must be set to `true` or `AuthorizationPolicy` must be configured |
| `AuthorizationPolicy` | `null` | ASP.NET Core authorization policy name. Required when `AllowAnonymousExplicit` is `false` |

## Dependencies

- `Headless.Messaging.Core`
- Embedded web UI assets

## Side Effects

- Exposes web endpoint at configured path (default: `/messaging`)
- Periodically polls message storage for statistics
- Anonymous by default — configure `AuthorizationPolicy` for production use
