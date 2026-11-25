# Headless.Messaging.RabbitMq

RabbitMQ transport provider for the messaging system.

## Why use this package

Enables Headless Bus and Queue delivery over RabbitMQ using lane-qualified exchanges, owned queues, routing keys, publisher confirms, consumer acknowledgements, and configurable queue arguments.

When dead-lettering is enabled, dead-letter queues use the separate `headless_dlq.` namespace.

## Install

```bash
dotnet add package Headless.Messaging.RabbitMq
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Messaging guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/messaging/rabbitmq.md#headlessmessagingrabbitmq)
