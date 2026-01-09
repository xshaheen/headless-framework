# Framework.Messaging.MassTransit

MassTransit integration for enterprise messaging with RabbitMQ, Azure Service Bus, and more.

## Problem Solved

Provides enterprise-grade message bus implementation using MassTransit, supporting multiple transports (RabbitMQ, Azure Service Bus, Amazon SQS) with advanced features like sagas, retries, and outbox patterns.

## Key Features

- MassTransit integration with framework interfaces
- Support for multiple transports
- Consumer registration and routing
- Saga support for distributed transactions
- Retry policies and error handling

## Installation

```bash
dotnet add package Framework.Messaging.MassTransit
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderPlacedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"]);
        cfg.ConfigureEndpoints(context);
    });
});
```

## Configuration

### appsettings.json

```json
{
  "RabbitMQ": {
    "Host": "localhost",
    "Username": "guest",
    "Password": "guest"
  }
}
```

## Dependencies

- `Framework.Messaging.Abstractions`
- `MassTransit`

## Side Effects

- Registers MassTransit hosted service
- Configures message consumers
