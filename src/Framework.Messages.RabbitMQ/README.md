# Framework.Messages.RabbitMQ

RabbitMQ transport provider for the messaging system.

## Problem Solved

Enables reliable message delivery using RabbitMQ with exchanges, queues, routing keys, and advanced AMQP features for flexible pub/sub patterns.

## Key Features

- **Exchange/Queue Model**: Flexible routing with topic, direct, fanout, and headers exchanges
- **Reliability**: Publisher confirms, consumer acknowledgments, and dead-letter exchanges
- **Auto-Provisioning**: Automatic exchange and queue creation
- **Clustering**: High availability with RabbitMQ clusters
- **Priority Queues**: Message priority support

## Installation

```bash
dotnet add package Framework.Messages.RabbitMQ
```

## Quick Start

```csharp
builder.Services.AddMessages(options =>
{
    options.UsePostgreSql("connection_string");

    options.UseRabbitMQ(rmq =>
    {
        rmq.HostName = "localhost";
        rmq.Port = 5672;
        rmq.UserName = "myapp_user"; // Required - cannot use 'guest'
        rmq.Password = "secure_password"; // Required - cannot use 'guest'
        rmq.VirtualHost = "/";
    });

    options.ScanConsumers(typeof(Program).Assembly);
});
```

> **Security Note:** Username and password must be configured explicitly. The default RabbitMQ credentials (`guest`/`guest`) are rejected to prevent accidental production deployments with insecure settings.

## Configuration

```csharp
options.UseRabbitMQ(rmq =>
{
    rmq.HostName = "localhost";
    rmq.Port = 5672;
    rmq.UserName = builder.Configuration["RabbitMq:UserName"]!; // From config
    rmq.Password = builder.Configuration["RabbitMq:Password"]!; // From config
    rmq.VirtualHost = "/";
    rmq.ExchangeName = "myapp.events";
    rmq.ConnectionFactoryOptions = factory =>
    {
        factory.AutomaticRecoveryEnabled = true;
        factory.NetworkRecoveryInterval = TimeSpan.FromSeconds(10);
    };
});
```

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
- **Priority queues**: Higher priority messages delivered first, breaking FIFO order
- **Redelivery after failure**: Failed messages may be redelivered out of order

### Recommendations

- For strict ordering: Use `ConsumerThreadCount = 1`
- For high throughput: Design consumers to handle out-of-order messages
- Consider Kafka or Azure Service Bus with sessions for stronger ordering guarantees

## Dependencies

- `Framework.Messages.Core`
- `RabbitMQ.Client`

## Side Effects

- Creates exchanges and queues if they don't exist
- Establishes persistent connections to RabbitMQ
- Configures dead-letter exchanges for failed messages
