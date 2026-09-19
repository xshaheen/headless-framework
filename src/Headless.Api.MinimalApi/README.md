# Headless.Api.MinimalApi

Framework integration for ASP.NET Core Minimal APIs with JSON configuration, validation filters, and exception handling.

## Why use this package

Provides consistent JSON serialization and validation for Minimal API endpoints matching the framework's conventions. Exception-to-ProblemDetails mapping is handled globally by `Headless.Api.Core`'s `HeadlessApiExceptionHandler` (registered via `AddHeadlessProblemDetails()`).

## Install

```bash
dotnet add package Headless.Api.MinimalApi
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [API & Web guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/api.md#headlessapiminimalapi)
