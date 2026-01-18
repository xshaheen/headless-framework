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
        rmq.UserName = "guest";
        rmq.Password = "guest";
        rmq.VirtualHost = "/";
    });

    options.ScanConsumers(typeof(Program).Assembly);
});
```

## Configuration

```csharp
options.UseRabbitMQ(rmq =>
{
    rmq.HostName = "localhost";
    rmq.Port = 5672;
    rmq.UserName = "guest";
    rmq.Password = "guest";
    rmq.VirtualHost = "/";
    rmq.ExchangeName = "myapp.events";
    rmq.ConnectionFactoryOptions = factory =>
    {
        factory.AutomaticRecoveryEnabled = true;
        factory.NetworkRecoveryInterval = TimeSpan.FromSeconds(10);
    };
});
```

## Dependencies

- `Framework.Messages.Core`
- `RabbitMQ.Client`

## Side Effects

- Creates exchanges and queues if they don't exist
- Establishes persistent connections to RabbitMQ
- Configures dead-letter exchanges for failed messages
