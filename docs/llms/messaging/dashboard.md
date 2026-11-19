---
domain: Messaging
packages: Messaging.Dashboard, Messaging.Dashboard.Consul, Messaging.Dashboard.K8s
---

# Messaging dashboard

> Part of [Messaging](../messaging.md). The Messaging module of the unified dashboard, and its Consul and Kubernetes node discovery.

## Headless.Messaging.Dashboard

The Messaging module of the unified dashboard. It shows published and received messages of the primary store and every additional outbox, with sidebar counts, Needs you items, Overview figures, id lookup, payloads, live changes, commands, and Operations.

Read [dashboards.md](../dashboards.md) for the shell, its authentication modes, and the production security boundary. [Messaging module](../dashboards/messaging-module.md) lists the module's options, object ids, count keys, commands, Operations, forwarding, and API.

### Setup

```bash
dotnet add package Headless.Messaging.Dashboard
```

Add the module to the shell beside `AddHeadlessMessaging(...)`. The order of the two calls does not matter. A host that calls `AddMessaging()` without `AddHeadlessMessaging(...)` fails to start with a message naming that call.

```csharp
using Headless.Dashboard;
using Headless.Messaging.Dashboard;

builder.Services.AddHeadlessMessaging(setup => setup.UsePostgreSql(builder.Configuration.GetConnectionString("Orders")!));
builder.Services.AddHeadlessDashboard(dashboard => dashboard
    .WithHostAuthentication(host => { host.ViewPolicy = "MessagingViewers"; host.OperatePolicy = "MessagingOperators"; })
    .AddMessaging());

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapHeadlessDashboard();
```

**Verify:** sign in as a user the view policy admits, open `{shell base}/messaging` (by default `/headless/messaging`), and confirm the module lists the `primary` store.

### Configuration

`AddMessaging(...)` takes `Action<MessagingDashboardOptions>`, its `IServiceProvider` overload, or an `IConfiguration` section. [Messaging module](../dashboards/messaging-module.md) lists every member: `ViewPolicy`, `OperatePolicy`, `NodeName`, `ChangeOverlapWindow`, `ChangePageSize`, `StoreTimeout`, and `Forwarding`.

### Behavior

- **Embedded front end.** The package embeds the module's views and serves them through the shell at `{shell base}/messaging`. It needs no separate deployment.
- **Authentication comes from the shell.** The module has none of its own. The shell's mode (`WithHostAuthentication`, `WithBasicAuth`, `WithApiKey`, or `WithNoAuth`) covers it. In host mode, `MessagingDashboardOptions.ViewPolicy` and `OperatePolicy` narrow who may read the module and who may run its commands. Empty policies inherit the shell's.
- **Every store.** The module reads the primary store, named `primary`, and every [additional outbox](storage.md#additional-outboxes) by its `AddOutbox(name)` name. A read across several stores names a store that does not answer within `StoreTimeout` instead of failing.
- **Other nodes.** With [forwarding](../dashboards/messaging-module.md#forwarding-to-other-nodes) and a discovery provider, each read and command can name another node, which serves its own rows. [Headless.Messaging.Dashboard.Consul](#headlessmessagingdashboardconsul) and [Headless.Messaging.Dashboard.K8s](#headlessmessagingdashboardk8s) supply discovery. `AddMessagingStandalone(...)` adds the module to a dashboard-only host with no stores of its own; see [Dashboard-only host](../dashboards/messaging-module.md#dashboard-only-host).
- **Live changes.** Each node reads its stores' changed rows on a poll and publishes them over the shell's live stream, so open views update without reloading.
- **Real-time throughput** reports `null`, not `0`, for every second the node did not measure, such as the seconds before it started.
- **Commands** re-send, re-execute, and delete message records through the [operator APIs](operations.md#operator-apis), so in host mode they need the operate policy. A command records its operator from the shell's signed-in user: the name, then the `NameIdentifier` or `sub` claim.

### Security

- The module shows every stored payload in full to anyone the view policy admits. This is one reason a payload must never carry a secret; see [Agent Rules](../messaging.md#agent-rules). Give the view policy only to users who may see payloads.
- Treat `WithNoAuth()` as development-only unless trusted network controls isolate the dashboard.

To move from the removed `MapHeadlessMessagingDashboard`, follow [Migrate from MapHeadlessMessagingDashboard](../dashboards/messaging-module.md#migrate-from-mapheadlessmessagingdashboard).

## Headless.Messaging.Dashboard.Consul

Consul node discovery for the Messaging module. Each node registers itself in Consul, and the module lists every catalog service tagged `messaging` as a node it can [forward](../dashboards/messaging-module.md#forwarding-to-other-nodes) to.

### Setup

```bash
dotnet add package Headless.Messaging.Dashboard.Consul
```

```csharp
services.AddHeadlessMessaging(setup =>
{
    // ... transport + storage registration ...
    setup.UseConsulDiscovery(options =>
    {
        options.DiscoveryServerHostName = "consul";
        options.CurrentNodeHostName = "orders-api";
        options.CurrentNodePort = 8080;
        options.NodeName = "orders-api";
    });
});
```

`UseConsulDiscovery(...)` is an extension on `MessagingSetupBuilder` in the `Headless.Messaging.Dashboard.Consul` namespace.

**Verify:** the service named by `NodeName` appears in the Consul catalog with a passing TCP check, and the Messaging module lists it as a node. A failed registration is only logged, so check the Consul catalog, not the host start.

### Configuration

`ConsulDiscoveryOptions`:

| Option | Default | Meaning |
| --- | --- | --- |
| `DiscoveryServerHostName`, `DiscoveryServerPort` | `localhost`, `8500` | The Consul agent HTTP API |
| `CurrentNodeHostName`, `CurrentNodePort` | `localhost`, `5000` | The address this node registers |
| `NodeName` | none | The service name this node registers under, and the name forwarding uses to recognize this node. Set it: Consul rejects a registration without a service name, and the failure is only logged |
| `NodeId` | `null` | Optional service ID. Consul uses `NodeName` when it is `null` |
| `Scheme` | `http` | The scheme the module calls discovered nodes over. Dashboard forwarding refuses an `http` node unless `AllowInsecurePeers` is set ([Forwarding to other nodes](../dashboards/messaging-module.md#forwarding-to-other-nodes)) |
| `CustomTags` | none | Extra tags merged with the built-in `Headless`, `Messaging`, `Client`, and `Dashboard` tags |

### Behavior

- At startup, the node registers in Consul with a TCP health check on `CurrentNodeHostName:CurrentNodePort` every 10 seconds, and deregistration after 30 seconds critical.
- Consul addresses nodes by service name alone, with no namespace. Forwarding resolves a node by name through Consul on each call, with no cache, and never forwards to this node's own `NodeName`.
- A discovery failure is logged and returns no nodes. Caller cancellation propagates.

## Headless.Messaging.Dashboard.K8s

Kubernetes Service discovery for the Messaging module, restricted to one namespace.

### Setup

```bash
dotnet add package Headless.Messaging.Dashboard.K8s
```

```csharp
services.AddHeadlessMessaging(setup =>
{
    // ... transport + storage registration ...
    setup.UseK8sDiscovery();
});
```

Grant the pod's service account RBAC permission to read Services in the configured namespace.

**Verify:** a Service labeled `headless.messaging.visibility: show` in the namespace appears as a node in the Messaging module. A missing RBAC grant or an unconfigured namespace shows no nodes.

### Configuration

`K8sDiscoveryOptions`, set through `UseK8sDiscovery(...)`:

| Option | Default | Meaning |
| --- | --- | --- |
| `K8sClientConfig` | `KubernetesClientConfiguration.BuildDefaultConfig()` | The client configuration used to query the cluster. Its namespace is the only namespace eligible for discovery and forwarding. Discovery fails closed when no namespace is configured |
| `ShowOnlyExplicitVisibleNodes` | `true` | Lists only Services labeled `headless.messaging.visibility:show`. Set `false` to list every Service |
| `Scheme` | `http` | The scheme (`http` or `https`) each discovered Service is called at. Forwarding refuses an `http` node unless `AllowInsecurePeers` is set, so a deployment that forwards serves the dashboard over HTTPS inside the cluster and sets `https` |

A dashboard-only host that shows the Services Kubernetes lists uses `AddHeadlessDashboard(d => d.AddMessagingStandalone(messaging => ..., k8s => ...))`; see [Dashboard-only host](../dashboards/messaging-module.md#dashboard-only-host).

### Behavior

- Each call queries the Kubernetes API for Services in the configured namespace.
- A forwarded call names the Service, never a client-composed endpoint. The server resolves that name in the configured namespace on each call and applies the visibility and port-label rules before forwarding, so a hidden, cross-namespace, or missing Service is refused.
