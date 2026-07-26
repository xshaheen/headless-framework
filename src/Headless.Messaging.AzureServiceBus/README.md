# Headless.Messaging.AzureServiceBus

Azure Service Bus transport provider for the messaging system.

## Problem Solved

Enables enterprise messaging using Azure Service Bus with topics, subscriptions, sessions, and advanced routing capabilities.

## Key Features

- **Topic/Subscription Model**: Pub/sub messaging with filters
- **Sessions**: Message ordering and state management
- **Auto-Provisioning**: Automatic topic and subscription creation
- **Advanced Routing**: Message routing rules and filters
- **Enterprise Features**: Transactions, duplicate detection, dead-lettering
- **Host-Cancellable Startup**: Client, topology, and processor setup honor host shutdown.
- **Shared Connection**: Bus and queue publishing and consumer processors all share one `ServiceBusClient` (one AMQP connection) per namespace, with per-destination cached senders and a shared administration client for topology provisioning; senders are drained before the client on shutdown, and consumers stop their processors without touching the shared client.

## Installation

```bash
dotnet add package Headless.Messaging.AzureServiceBus
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.Bus.ForConsumersFromAssemblyContaining<Program>();
    options.UseSqlServer("connection_string");

    options.UseAzureServiceBus(asb =>
    {
        asb.ConnectionString = "Endpoint=sb://namespace.servicebus.windows.net/;...";
        asb.TopicPath = "myapp";
    });
});
```

## Configuration

```csharp
options.UseAzureServiceBus(asb =>
{
    asb.ConnectionString = "connection_string";
    asb.TopicPath = "myapp-topic";
    asb.EnableSessions = true; // Required for ordered delivery
    asb.TokenCredential = credential; // Azure.Core.TokenCredential when not using ConnectionString
});

options.Bus.ForMessage<OrderEvent>(message =>
    message
        .MessageName("orders.events")
        .UseAzureServiceBus(asb => asb.PartitionKey(order => order.CustomerId.ToString()))
);
```

`PartitionKey(...)` stamps `AzureServiceBusMessagingHeaders.PartitionKey` (`headless-asb-partition-key`) during publish and is limited to 128 characters. When sessions are enabled, Azure Service Bus requires `PartitionKey` to match `AzureServiceBusMessagingHeaders.SessionId`. If you omit `SessionId`, the provider now falls back to `PartitionKey` before the framework message id so partitioned session publishes do not fail by default. The selector output is broker-visible metadata, so do not put secrets or raw PII in it.

The provider declares immutable Bus and Queue capabilities with independent topic/queue topology. The same contract and logical name may therefore be configured independently through `options.Bus` and `options.Queue`.

## Message Ordering

Azure Service Bus provides **FIFO ordering when sessions are enabled**:

### Session-Based Ordering

Enable sessions for guaranteed message ordering within a session:

```csharp
options.UseAzureServiceBus(asb =>
{
    asb.EnableSessions = true;
    asb.MaxConcurrentSessions = 8; // Concurrent sessions for throughput
    asb.SessionIdleTimeout = TimeSpan.FromMinutes(5);
});
```

### Publishing Ordered Messages

All messages require a session ID when sessions are enabled:

```csharp
// Publish with session ID for ordered delivery
await publisher.PublishAsync(
    order,
    new PublishOptions
    {
        MessageName = "orders.events",
        Headers = new Dictionary<string, string?> { [AzureServiceBusMessagingHeaders.SessionId] = order.CustomerId.ToString() },
    }
);
```

When you also configure `PartitionKey(...)`, return the same value as `AzureServiceBusMessagingHeaders.SessionId` while sessions are enabled.

### Consumer Configuration

```csharp
options.ConsumerThreadCount = 1; // Recommended for strict ordering
options.EnableSubscriberParallelExecute = false;
```

### Ordering Guarantees

- **Sessions enabled + same session ID**: Strict FIFO ordering
- **Sessions disabled**: No ordering guarantees
- **Multiple concurrent sessions**: Each session is ordered independently

## Messaging Semantics

- Publish forwards the serialized body and headers as Service Bus messages.
- Delay stays in the core pipeline unless you add broker scheduling separately.
- Commit completes the message.
- Reject abandons the message. Redelivery and dead-lettering follow entity lock and delivery settings.
- Headless disables Azure SDK auto-complete internally and settles messages explicitly after durable receive storage and handler outcome.
- `AutoProvision` creates topics, subscriptions, and rules when enabled.
- `SubscribeAsync(...)` keeps subscription rules aligned with topic names and SQL filters.
- Use `AzureServiceBusMessagingHeaders.SessionId` for ordered delivery. `ConsumerThreadCount` only affects parallelism around those sessions.
- Entity names, property sizes, and payload limits follow Azure Service Bus limits.

**Registration overloads:** `UseAzureServiceBus(...)` accepts the standard trio — an `IConfiguration` section, an `Action<AzureServiceBusMessagingOptions>` delegate, or an `Action<AzureServiceBusMessagingOptions, IServiceProvider>` delegate — plus the connection-string convenience form. Authentication is an either/or contract: set either `ConnectionString` or both `Namespace` and `TokenCredential` (both nullable `string?`; the validator enforces exactly one mode at start).

## Dependencies

- `Headless.Messaging.Core`
- `Azure.Messaging.ServiceBus`

## Side Effects

- Creates Service Bus topics and subscriptions if they don't exist
- Establishes a persistent connection to Azure Service Bus (one shared client per namespace across publishing and consuming)
- Configures message routing rules and filters
