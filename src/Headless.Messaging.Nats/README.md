# Headless.Messaging.NATS

NATS JetStream transport provider for the Headless messaging system.

## Key Features

- **Lightweight**: Minimal resource footprint, cloud-native
- **JetStream**: Persistent streams with at-least-once delivery
- **Subject Routing**: Hierarchical topic patterns (e.g., `orders.*.created`)
- **Connection Pooling**: Round-robin pool for publish throughput

## Installation

```bash
dotnet add package Headless.Messaging.NATS
```

## Quick Start

```csharp
builder.Services.AddMessages(options =>
{
    options.UsePostgreSql("connection_string");

    options.UseNats(nats =>
    {
        nats.Servers = "nats://localhost:4222";
    });

    options.ScanConsumers(typeof(Program).Assembly);
});
```

## Configuration

```csharp
options.UseNats(nats =>
{
    nats.Servers = "nats://localhost:4222,nats://localhost:4223";
    nats.ConnectionPoolSize = 10;

    // Customize stream creation (defaults to File storage)
    nats.StreamOptions = config =>
    {
        config.Storage = StreamConfigStorage.Memory; // for dev/testing
    };

    // Customize NATS connection
    nats.ConfigureConnection = opts => opts with
    {
        ConnectTimeout = TimeSpan.FromSeconds(10),
    };
});
```

### Stream Auto-Creation

By default, consumer clients create JetStream streams with wildcard subjects on startup
(`AutoCreateStreams` via `EnableSubscriberClientStreamAndSubjectCreation`). For production
deployments requiring fine-grained control, disable this and manage streams externally:

```csharp
nats.EnableSubscriberClientStreamAndSubjectCreation = false;
```

## Dependencies

- `Headless.Messaging.Core`
- `NATS.Net`

## Side Effects

- Establishes persistent connections to NATS servers
- Creates JetStream streams and consumers if enabled
- Subscribes to subjects for message consumption
