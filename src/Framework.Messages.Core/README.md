# Framework.Messages.Core

Core implementation of the type-safe messaging system with outbox pattern, message processing, and consumer lifecycle management.

## Problem Solved

Provides the foundational runtime for reliable distributed messaging with transactional outbox, automatic retries, delayed delivery, and type-safe consumer orchestration across multiple transport providers.

## Key Features

- **Outbox Publisher**: Transactional message publishing with database consistency
- **Consumer Management**: Automatic registration, invocation, and lifecycle handling
- **Message Processing**: Retry processor, delayed message scheduler, transport health checks
- **Type-Safe Dispatch**: Reflection-free consumer invocation via compile-time generated code
- **Extension System**: Pluggable storage and transport providers
- **Bootstrapper**: Hosted service for startup and shutdown coordination

## Installation

```bash
dotnet add package Framework.Messages.Core
```

## Quick Start

```csharp
// Register messaging with storage and transport
builder.Services.AddMessages(options =>
{
    // Core configuration
    options.SucceedMessageExpiredAfter = 24 * 3600;
    options.FailedRetryCount = 50;

    // Add storage (required)
    options.UsePostgreSql("connection_string");

    // Add transport (required)
    options.UseRabbitMQ(rmq =>
    {
        rmq.HostName = "localhost";
        rmq.Port = 5672;
    });

    // Register consumers
    options.ScanConsumers(typeof(Program).Assembly);
});

// Publish messages with outbox
public sealed class OrderService(IOutboxPublisher publisher, IOutboxTransaction transaction)
{
    public async Task PlaceOrderAsync(Order order, CancellationToken ct)
    {
        using (transaction.Begin())
        {
            // Database changes and message publish are atomic
            await publisher.PublishAsync("orders.placed", order, cancellationToken: ct);
            await transaction.CommitAsync(ct);
        }
    }
}
```

## Configuration

Register in `Program.cs`:

```csharp
builder.Services.AddMessages(options =>
{
    options.FailedRetryCount = 50;
    options.SucceedMessageExpiredAfter = 24 * 3600;
    options.ConsumerThreadCount = 1;
    options.DefaultGroupName = "myapp";
});
```

## Dependencies

- `Framework.Messages.Abstractions`
- `Framework.Base`
- `Framework.Checks`
- Transport package (RabbitMQ, Kafka, etc.)
- Storage package (PostgreSql, SqlServer, etc.)

## Side Effects

- Starts background hosted service for message processing
- Creates database tables for outbox storage (via storage provider)
- Establishes transport connections (via transport provider)
