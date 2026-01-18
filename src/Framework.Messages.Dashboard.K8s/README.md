# Framework.Messages.Dashboard.K8s

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
dotnet add package Framework.Messages.Dashboard.K8s
```

## Quick Start

```csharp
builder.Services.AddMessages(options =>
{
    options.UsePostgreSql("connection_string");
    options.UseRabbitMQ(config);

    options.UseDashboard();

    options.UseK8sDiscovery(k8s =>
    {
        k8s.Namespace = "production";
        k8s.ServiceName = "messaging-service";
    });

    options.ScanConsumers(typeof(Program).Assembly);
});
```

## Configuration

```csharp
options.UseK8sDiscovery(k8s =>
{
    k8s.Namespace = "production";
    k8s.ServiceName = "messaging-service";
    k8s.Port = 8080;
});
```

## Dependencies

- `Framework.Messages.Dashboard`
- `KubernetesClient`

## Side Effects

- Queries Kubernetes API for pod endpoints
- Requires appropriate RBAC permissions (read pods/endpoints)
- Periodically polls for cluster topology changes
