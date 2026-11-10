# Headless.Messaging.Dashboard.Consul

Consul-backed node discovery for the Messaging module of the Headless dashboard in multi-node deployments.

## Why use this package

Registers each messaging node in Consul and lists its peers to the Messaging module, so one dashboard can read and act on every node through forwarding, without the module package itself depending on the Consul client.

## Install

```bash
dotnet add package Headless.Messaging.Dashboard.Consul
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Messaging guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/messaging.md#headlessmessagingdashboardconsul)
