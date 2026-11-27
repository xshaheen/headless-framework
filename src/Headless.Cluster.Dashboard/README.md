# Headless.Cluster.Dashboard

The Cluster module of the Headless dashboard: the node incarnations Coordination reports with their liveness and metadata agreement, and the serving host's health checks with their recent probe results.

## Why use this package

Operators see which nodes the membership store holds and whether each is alive, suspected, or dead, whether live nodes agree on a metadata value such as the deployed version, and every health check of the host with its last 30 results. They probe one check or all of them on demand, and the shell writes each probe to the operator log. The module is embedded in the host and served by `Headless.Dashboard` under its base path and sign-in.

## Install

```bash
dotnet add package Headless.Cluster.Dashboard
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Coordination guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/coordination.md#headlessclusterdashboard)
- [Dashboards guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/dashboards/cluster-module.md#cluster-module)
