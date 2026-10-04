# Headless.Hosting

Core hosting utilities and extensions for ASP.NET Core applications.

## Why use this package

Provides essential DI extensions, configuration helpers, options validation, and seeder infrastructure to reduce boilerplate in application startup and configuration. Also owns the framework-wide GUID-generator registration (`AddHeadlessGuidGenerator()`) and the `LogState` structured-logging scope builder with its `ILogger` extensions.

## Install

```bash
dotnet add package Headless.Hosting
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Utilities guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/utilities.md#headlesshosting)
