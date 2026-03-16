# Headless.Jobs.Dashboard

Monitoring dashboard for Headless.Jobs with built-in authentication options and real-time updates.

## Installation

```bash
dotnet add package Headless.Jobs.Dashboard
```

## Minimal Setup

```csharp
using Headless.Jobs.DependencyInjection;

builder.Services
    .AddJobs()
    .AddDashboard(dashboard =>
    {
        dashboard.SetBasePath("/jobs-dashboard");
        dashboard.WithHostAuthentication();
    });

var app = builder.Build();
app.UseJobs();
```

The dashboard API, SignalR hub, and UI are mounted under the configured base path.

## 🚀 Quick Examples

### No Authentication (Public Dashboard)
```csharp
services.AddJobs<MyTimeJob, MyCronJob>(config =>
{
    config.AddDashboard(dashboard =>
    {
        // No authentication setup = public dashboard
    });
});
```

### Basic Authentication
```csharp
services.AddJobs<MyTimeJob, MyCronJob>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithBasicAuth("admin", "secret123");
    });
});
```

### API Key Authentication
```csharp
services.AddJobs<MyTimeJob, MyCronJob>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithApiKey("my-secret-api-key-12345");
    });
});
```

### Use Host Application's Authentication
```csharp
services.AddJobs<MyTimeJob, MyCronJob>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithHostAuthentication();
    });
});
```

### Use Host Authentication with Custom Policy
```csharp
services.AddJobs<MyTimeJob, MyCronJob>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithHostAuthentication("AdminPolicy");
    });
});
```

## 🔧 Fluent API Methods

- `WithBasicAuth(username, password)` - Enable username/password authentication
- `WithApiKey(apiKey)` - Enable API key authentication
- `WithHostAuthentication(policy)` - Use your app's existing auth with optional policy (e.g., "AdminPolicy")
- `SetBasePath(path)` - Set dashboard URL path
- `SetBackendDomain(domain)` - Set backend API domain
- `SetCorsPolicy(policy)` - Configure CORS

## 🔒 How It Works

The dashboard automatically detects your authentication method:

1. **No auth configured** → Public dashboard
2. **Basic auth configured** → Username/password login
3. **Bearer token configured** → API key authentication
4. **Host auth configured** → Delegates to your app's auth system

## 🌐 Frontend Integration

The frontend automatically adapts based on your backend configuration:
- Shows appropriate login UI
- Handles SignalR authentication
- Supports both header and query parameter auth (for WebSockets)

## Error Monitoring

Dashboard tracks runtime outcomes from the scheduler, including:

- Failed jobs after retry exhaustion
- Cancelled jobs
- Skipped jobs (for example, overlapping cron occurrences)
- Retry count and latest execution details

Use Dashboard for operational triage, then combine with `Headless.Jobs.OpenTelemetry` for trace-level diagnostics.

That's it! Simple and clean. 🎉
