# Headless.Logging.Serilog

Serilog defaults and configuration helpers for Headless applications.

## Why use this package

Setting up Serilog correctly for ASP.NET Core requires wiring a bootstrap logger (to catch startup failures), then replacing it with a reloadable production logger that reads from `IConfiguration`, applies enrichers, routes levels to separate files, and adjusts the console theme per environment. `SerilogFactory` encodes this two-phase setup as tested, opinionated extension methods so applications get correct defaults without repeating the configuration boilerplate.

## Install

```bash
dotnet add package Headless.Logging.Serilog
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Logging guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/logging.md#headlessloggingserilog)
