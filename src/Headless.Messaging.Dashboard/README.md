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
- **5-Mode Auth**: None, Basic, API Key, Host, Custom (shared with Jobs Dashboard)

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
        dashboard.WithBasicAuth("admin", "secret123");
    });

    options.SubscribeFromAssemblyContaining<Program>();
});

// Access dashboard at: http://localhost:5000/messaging
```

## Authentication Modes

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
    dashboard.WithCustomAuth(token => ValidateToken(token));
});
```

## Fluent API Methods

- `WithNoAuth()` - Public dashboard (no authentication)
- `WithBasicAuth(username, password)` - Enable username/password authentication
- `WithApiKey(apiKey)` - Enable API key authentication
- `WithHostAuthentication(policy?)` - Use your app's existing auth with optional policy
- `WithCustomAuth(validator)` - Custom authentication with validation function
- `WithSessionTimeout(minutes)` - Set session timeout (default: 60 minutes)
- `SetBasePath(path)` - Set dashboard URL path (default: `/messaging`)
- `SetStatsPollingInterval(ms)` - Stats polling interval (default: 2000ms)
- `SetCorsPolicy(policy)` - Configure CORS

## Configuration

| Method | Default | Description |
|--------|---------|-------------|
| `SetBasePath` | `/messaging` | URL path for the dashboard |
| `SetStatsPollingInterval` | `2000` | Stats endpoint polling interval (ms) |
| `WithNoAuth` | (default) | No authentication — public dashboard |
| `SetCorsPolicy` | `null` | CORS policy for cross-origin requests |
| `WithSessionTimeout` | `60` | Session timeout in minutes |

## Dependencies

- `Headless.Messaging.Core`
- `Headless.Dashboard.Authentication` (shared auth with Jobs Dashboard)
- Embedded web UI assets

## Side Effects

- Exposes web endpoint at configured path (default: `/messaging`)
- Periodically polls message storage for statistics
- No authentication by default — configure auth for production use
