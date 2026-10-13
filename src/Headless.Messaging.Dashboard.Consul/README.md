# Headless.Messaging.Dashboard.Consul

Consul-backed node discovery for the messaging dashboard in multi-node deployments.

## Why use this package

Registers each messaging node in Consul and lists its peers in the dashboard, so one dashboard can switch between nodes and proxy requests to them, without the dashboard package itself depending on the Consul client.

## Install

```bash
dotnet add package Headless.Messaging.Dashboard.Consul
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Messaging guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/messaging.md#headlessmessagingdashboardconsul)
