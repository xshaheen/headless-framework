# Framework.Messages.AzureServiceBus

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
dotnet add package Framework.Messages.AzureServiceBus
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
    asb.EnableSessions = true;
    asb.ManagementTokenProvider = tokenProvider;
});
```

## Dependencies

- `Framework.Messages.Core`
- `Azure.Messaging.ServiceBus`

## Side Effects

- Creates Service Bus topics and subscriptions if they don't exist
- Establishes persistent connections to Azure Service Bus
- Configures message routing rules and filters
