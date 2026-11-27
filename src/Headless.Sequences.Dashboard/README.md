# Headless.Sequences.Dashboard

The Sequences module of the Headless dashboard: a read-only view of every stored sequence counter across host and tenant scopes, with its value, last formatted number, partition, policy, and next reset.

## Why use this package

Support staff get asked for the last invoice number of a tenant, or when the order counter restarts. Without a screen they query the counter table by hand, or call `NextAsync` to learn a value, which leaves a real gap in a gap-free series. This module lists the counters through `ISequenceInspector`, which never takes a number, filters them by mode, scope, current partition, and text, and shows each counter's policy and the partitions of its name. It is embedded in the host and served by `Headless.Dashboard` under its base path and sign-in, and it has no command that changes a counter.

## Install

```bash
dotnet add package Headless.Sequences.Dashboard
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Sequences guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/sequences.md#headlesssequencesdashboard)
- [Dashboards guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/dashboards/sequences-module.md#sequences-module)
