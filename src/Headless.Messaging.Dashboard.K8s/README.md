# Headless.Messaging.Dashboard.K8s

Kubernetes-aware node discovery for the messaging dashboard in clustered environments.

## Problem Solved

Enables automatic discovery and monitoring of messaging nodes in Kubernetes clusters by querying pod endpoints for multi-instance dashboard visibility.

## Key Features

- **Auto-Discovery**: Automatically finds messaging nodes in Kubernetes namespace
- **Service Integration**: Uses Kubernetes Service for node enumeration
- **Health Monitoring**: Tracks node availability and status
- **Dynamic Updates**: Reflects pod scaling events in real-time
- **No Configuration**: Works with default Kubernetes service discovery

## Installation

```bash
dotnet add package Headless.Messaging.Dashboard.K8s
```

## Quick Start

### With Full Messaging Stack

```csharp
builder.Services.AddMessaging(options =>
{
    options.UsePostgreSql("connection_string");
    options.UseRabbitMQ(config);

    options.UseDashboard(dashboard =>
    {
        dashboard.WithBasicAuth("admin", "secret");
    });

    options.UseK8sDiscovery(k8s =>
    {
        k8s.ShowOnlyExplicitVisibleNodes = true;
    });

    options.SubscribeFromAssemblyContaining<Program>();
});
```

### Standalone Dashboard (View-Only)

```csharp
builder.Services.AddMessagingDashboardStandalone(
    configure: dashboard =>
    {
        dashboard.WithNoAuth();
        dashboard.SetBasePath("/messaging");
    },
    k8SOption: k8s =>
    {
        k8s.ShowOnlyExplicitVisibleNodes = true;
    }
);
```

## Dependencies

- `Headless.Messaging.Dashboard`
- `KubernetesClient`

## Side Effects

- Queries Kubernetes API for pod endpoints
- Requires appropriate RBAC permissions (read pods/endpoints)
- Periodically polls for cluster topology changes
