# Headless.Messaging.RabbitMq

RabbitMQ transport provider for the messaging system.

## Problem Solved

Enables Headless Bus and Queue delivery over RabbitMQ using lane-qualified exchanges, owned queues, routing keys, publisher confirms, consumer acknowledgements, and configurable queue arguments.

## Key Features

- **Lane-Isolated Topology**: Declares a Bus topic exchange and Queue direct exchange, with lane-qualified routing keys and owned queues
- **Reliability**: Optional publisher confirms that await broker acknowledgments or negative acknowledgments, plus consumer acknowledgments and rejects
- **Auto-Provisioning**: Automatic exchange and queue creation
- **Clustering**: Comma-separated broker host names for RabbitMQ cluster connectivity
- **Queue Arguments**: Queue TTL, queue mode, and queue type are exposed through `RabbitMqMessagingOptions.QueueArguments`
- **Consumer QoS**: Global and per-consumer prefetch configuration
- **Host-Cancellable Startup**: Cancellation flows through connection, channel, exchange, queue, and binding operations.

## Installation

```bash
dotnet add package Headless.Messaging.RabbitMq
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.Bus.ForMessage<OrderPlaced>(message =>
        message.Consumer<OrderPlacedConsumer>(consumer =>
            consumer.ConsumerIdentity("orders.order-placed").ContractVersion("v1")
        )
    );
    options.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
    options.UsePostgreSql("connection_string");

    options.UseRabbitMq(rmq =>
    {
        rmq.HostName = "localhost";
        rmq.Port = 5672;
        rmq.UserName = "myapp_user"; // Required - cannot use 'guest'
        rmq.Password = "secure_password"; // Required - cannot use 'guest'
        rmq.VirtualHost = "/";
    });
});
```

> **Security Note:** Username and password must be configured explicitly. The default RabbitMQ credentials (`guest`/`guest`) are rejected to prevent accidental production deployments with insecure settings.

## Configuration

```csharp
options.UseRabbitMq(rmq =>
{
    rmq.HostName = "localhost";
    rmq.Port = 5672;
    rmq.UserName = builder.Configuration["RabbitMq:UserName"]!; // From config
    rmq.Password = builder.Configuration["RabbitMq:Password"]!; // From config
    rmq.VirtualHost = "/";
    rmq.ExchangeName = "myapp.events";
    rmq.PublishConfirms = true; // Publish completes only after the broker acknowledges or rejects it
    rmq.ConnectionFactoryOptions = factory =>
    {
        factory.AutomaticRecoveryEnabled = true;
        factory.NetworkRecoveryInterval = TimeSpan.FromSeconds(10);
    };
});

options.Bus.ForMessage<OrderEvent>(message =>
    message
        .MessageName("orders.events")
        .Consumer<OrderProjection>(consumer =>
            consumer.Group("orders-projection").UseRabbitMq(rabbit => rabbit.PrefetchCount(20))
        )
);
```

Consumer-side RabbitMQ knobs attach to the consumer registration:

```csharp
options.Bus.ForMessage<OrderEvent>(message =>
    message.Consumer<OrderProjection>(consumer =>
        consumer.Group("orders-projection").UseRabbitMq(rabbit => rabbit.PrefetchCount(20))
    )
);
```

RabbitMQ supports the same contract and logical name on both lanes without cross-delivery. For a configured base exchange such as `myapp.events`, the provider creates `myapp.events.bus` (topic) and `myapp.events.queue` (direct), publishes with `bus.{logical-name}` / `queue.{logical-name}` routing keys, and owns `bus.{subscriber-group}` / `queue.{logical-name}` queues.

This topology replaces the pre-cutover shared topic exchange. Before deployment, stop old and new publishers, inventory producer/consumer versions and auto-provision permissions, drain every legacy exchange-bound queue to a measured zero-ready/zero-unacknowledged signal, then deploy consumers before publishers. Aborting is safe only before the first lane-qualified publication. After that point recovery is roll-forward-only: restore the new consumers, reconcile legacy and lane-qualified queue counts, and do not delete legacy entities until the deployment owner signs off.

### Security Best Practices

- **Never hardcode credentials** - use environment variables, configuration files, or secret management services
- **Avoid default credentials** - the framework rejects `guest`/`guest` to prevent security issues
- **Use strong passwords** - RabbitMQ passwords should be complex and unique
- **Restrict permissions** - create application-specific users with minimal required permissions
- **Enable TLS** - use encrypted connections in production environments

## Message Ordering

RabbitMQ provides **limited ordering guarantees**:

### Single Consumer Ordering

Messages are delivered in FIFO order to a single consumer on a single channel:

```csharp
// Configure for sequential processing
options.ConsumerThreadCount = 1; // Single consumer thread
options.EnableSubscriberParallelExecute = false; // No parallel execution
```

### Ordering Guarantees

- **Single consumer**: Messages arrive in publication order
- **Multiple consumers (`ConsumerThreadCount > 1`)**: No ordering guarantee; concurrent processing
- **Broker policies and queue arguments**: Broker-side settings can change observable delivery order
- **Redelivery after failure**: Failed messages may be redelivered out of order

### Recommendations

- For strict ordering: Use `ConsumerThreadCount = 1`
- For high throughput: Design consumers to handle out-of-order messages
- Consider Kafka or Azure Service Bus with sessions for stronger ordering guarantees

## Messaging Semantics

- Publish sends the serialized body to the lane-qualified exchange and preserves logical names, headers, and payloads.
- Delay stays in the core pipeline unless you add RabbitMQ delayed-message plugins yourself.
- Commit sends `BasicAck`.
- Reject sends `BasicReject(requeue: true)`. Dead-letter behavior follows queue arguments and broker policies.
- Consumer startup declares the Bus topic or Queue direct exchange and its lane-owned queue. `SubscribeAsync(...)` binds lane-qualified routing keys to that queue.
- Higher `ConsumerThreadCount` increases concurrency but weakens observable ordering guarantees.
- The configured `ExchangeName` is a base name; `.bus` and `.queue` are appended after the messaging-version suffix. Routing keys and queue names receive matching `bus.` / `queue.` prefixes. Headers and durable/wire identity remain unchanged.

**Registration overloads:** `UseRabbitMq(...)` accepts the standard trio — an `IConfiguration` section, an `Action<RabbitMqMessagingOptions>` delegate, or an `Action<RabbitMqMessagingOptions, IServiceProvider>` delegate — plus the host-name convenience form. `UserName` and `Password` are `required` and must be set explicitly; the validator rejects the default `guest`/`guest` credentials.

## Dependencies

- `Headless.Messaging.Core`
- `RabbitMQ.Client`

## Side Effects

- Creates lane-qualified exchanges and queues if they don't exist
- Binds routing keys for configured consumers
- Establishes persistent connections to RabbitMQ
- Rejects valid handler failures with `BasicReject(requeue: true)`; malformed transport envelopes use `BasicReject(requeue: false)` so they cannot create a requeue storm. Dead-letter behavior follows queue arguments and broker policies outside this package
