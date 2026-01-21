# Headless.Messaging.NATS

NATS messaging system transport provider for the messaging system.

## Problem Solved

Enables lightweight, cloud-native messaging using NATS with JetStream for persistence, subjects for routing, and NATS Streaming for reliable delivery.

## Key Features

- **Lightweight**: Minimal resource footprint
- **Cloud-Native**: Kubernetes-friendly, easy clustering
- **JetStream**: Persistent streams with at-least-once delivery
- **Subject Routing**: Hierarchical topic patterns (e.g., `orders.*.created`)
- **Request-Reply**: Built-in RPC support

## Installation

```bash
dotnet add package Headless.Messaging.NATS
```

## Quick Start

```csharp
builder.Services.AddMessages(options =>
{
    options.UsePostgreSql("connection_string");

    options.UseNATS(nats =>
    {
        nats.Servers = "nats://localhost:4222";
    });

    options.ScanConsumers(typeof(Program).Assembly);
});
```

## Configuration

```csharp
options.UseNATS(nats =>
{
    nats.Servers = "nats://localhost:4222,nats://localhost:4223";
    nats.ConnectionPoolSize = 10;
    nats.EnableJetStream = true;
});
```

## Dependencies

- `Headless.Messaging.Core`
- `NATS.Client`

## Side Effects

- Establishes persistent connections to NATS servers
- Creates JetStream streams if enabled
- Subscribes to subjects for message consumption
