# Headless.Dashboard

One operations dashboard for a Headless host, plus the authentication the Messaging dashboard uses.

## Why use this package

`AddHeadlessDashboard` and `MapHeadlessDashboard` serve every registered feature module under one base path, with one sign-in, one session, live updates that reach every node, and safe bulk commands. The dashboard stores nothing of its own. Operators sign in through the host's own login, Basic, or an API key. The package also ships the authentication the Messaging dashboard uses. `Headless.Jobs.Dashboard` adds the Jobs module.

## Install

```bash
dotnet add package Headless.Dashboard
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Dashboards guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/dashboards.md#headlessdashboard)
