# Headless.Leases.Dashboard

The Leases module of the Headless dashboard: the fenced leases the fencing store holds, in every state, and the distributed locks the lock provider reports as held. It only reads.

## Why use this package

During an incident, operators see which leases are live, which expired and wait for a takeover or a sweep, and how often a resource's work was taken over, without querying the lease table by hand. They open one lease to read its holder, generation, database-clock times, and progress size, and see which distributed locks are held right now with their time to live. The module is embedded in the host and served by `Headless.Dashboard` under its base path and sign-in.

## Install

```bash
dotnet add package Headless.Leases.Dashboard
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Fencing guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/fencing.md#headlessleasesdashboard)
- [Dashboards guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/dashboards/leases-module.md#leases-module)
