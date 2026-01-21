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

## Installation

```bash
dotnet add package Headless.Messaging.AzureServiceBus
```

## Quick Start

```csharp
builder.Services.AddMessages(options =>
{
    options.UseSqlServer("connection_string");

    options.UseAzureServiceBus(asb =>
    {
        asb.ConnectionString = "Endpoint=sb://namespace.servicebus.windows.net/;...";
        asb.TopicPath = "myapp";
    });

    options.ScanConsumers(typeof(Program).Assembly);
});
```

## Configuration

```csharp
options.UseAzureServiceBus(asb =>
{
    asb.ConnectionString = "connection_string";
    asb.TopicPath = "myapp-topic";
    asb.EnableSessions = true; // Required for ordered delivery
    asb.ManagementTokenProvider = tokenProvider;
});
```

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
await publisher.PublishAsync("orders.events", order,
    headers: new Dictionary<string, string>
    {
        { AzureServiceBusHeaders.SessionId, order.CustomerId.ToString() }
    });
```

### Consumer Configuration

```csharp
options.ConsumerThreadCount = 1; // Recommended for strict ordering
options.EnableSubscriberParallelExecute = false;
```

### Ordering Guarantees

- **Sessions enabled + same session ID**: Strict FIFO ordering
- **Sessions disabled**: No ordering guarantees
- **Multiple concurrent sessions**: Each session is ordered independently

## Dependencies

- `Headless.Messaging.Core`
- `Azure.Messaging.ServiceBus`

## Side Effects

- Creates Service Bus topics and subscriptions if they don't exist
- Establishes persistent connections to Azure Service Bus
- Configures message routing rules and filters
