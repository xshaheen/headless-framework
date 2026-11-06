# Headless.Jobs.Dashboard

The Jobs module of the Headless dashboard: runs, cron jobs, functions, and live nodes of `Headless.Jobs`, with failures grouped by cause and live changes from every node.

## Why use this package

Operators see every job run in the same dashboard as the rest of the application: failed runs grouped by cause, each attempt's exception and node, and cron schedules with their next fires. They can requeue, cancel, or delete one run or every run a filter matches. The module is embedded in the host and served by `Headless.Dashboard` under its base path and sign-in.

## Install

```bash
dotnet add package Headless.Jobs.Dashboard
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Jobs (Background Jobs) guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/jobs.md#headlessjobsdashboard)
- [Dashboards guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/dashboards.md#jobs-module)
