# Headless.Messaging.Dashboard

The Messaging module of the Headless dashboard: published and received messages of the primary store and every additional outbox, with failures grouped by cause and live changes from every node.

## Why use this package

Operators see every message in the same dashboard as the rest of the application: failed messages grouped by cause, each message's payload and attempts, scheduled deliveries, and the outboxes the host registers. They can re-send, re-execute, or delete one message or every message a filter matches. The module is embedded in the host and served by `Headless.Dashboard` under its base path and sign-in.

## Install

```bash
dotnet add package Headless.Messaging.Dashboard
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Messaging guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/messaging.md#headlessmessagingdashboard)
- [Dashboards guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/dashboards.md#messaging-module)
