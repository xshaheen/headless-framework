# Headless.Messaging.Dashboard.K8s

Kubernetes-aware node discovery for the messaging dashboard in clustered environments.

## Problem Solved

Enables automatic discovery and monitoring of messaging nodes in Kubernetes clusters by querying Services for multi-instance dashboard visibility.

## Key Features

- **Auto-Discovery**: Automatically finds messaging nodes in Kubernetes namespace
- **Service Integration**: Uses Kubernetes Service for node enumeration
- **Health Monitoring**: Tracks node availability and status
- **Dynamic Updates**: Reflects pod scaling events in real-time
- **Default Cluster Config**: Uses the ambient Kubernetes client configuration by default

## Installation

```bash
dotnet add package Headless.Messaging.Dashboard.K8s
```

## Quick Start

### With Full Messaging Stack

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.ForMessagesFromAssemblyContaining<Program>();
    options.UsePostgreSql("connection_string");
    options.UseRabbitMq(config);

    options.UseDashboard(dashboard =>
    {
        dashboard.WithBasicAuth("admin", "secret");
    });

    options.UseK8sDiscovery(k8s =>
    {
        k8s.ShowOnlyExplicitVisibleNodes = true;
    });
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
    configureK8s: k8s =>
    {
        k8s.ShowOnlyExplicitVisibleNodes = true;
    }
);
```

## Configuration

`UseK8sDiscovery(...)` configures `K8sDiscoveryOptions`:

- `K8sClientConfig` — Kubernetes client configuration used to query the cluster. Defaults to `KubernetesClientConfiguration.BuildDefaultConfig()`.
- `ShowOnlyExplicitVisibleNodes` — when `true` (default), only Services labeled `headless.messaging.visibility:show` are listed as visible dashboard nodes. Set to `false` to show all discovered Services.

## Dependencies

- `Headless.Messaging.Dashboard`
- `KubernetesClient`

## Side Effects

- Queries Kubernetes API for Services
- Requires appropriate RBAC permissions (read services/namespaces)
- Periodically polls for cluster topology changes
