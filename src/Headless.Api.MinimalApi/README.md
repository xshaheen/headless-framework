# Headless.Api.MinimalApi

Framework integration for ASP.NET Core Minimal APIs with JSON configuration, mediator dispatch, `ApiResult` responses, and validation filters.

## Why use this package

Provides consistent JSON serialization, validation, and one-line mediator endpoints whose `ApiResult` responses document themselves in OpenAPI, matching the framework's conventions. Exception-to-ProblemDetails mapping is handled globally by `Headless.Api`'s `HeadlessApiExceptionHandler` (registered via `AddHeadlessProblemDetails()`).

## Install

```bash
dotnet add package Headless.Api.MinimalApi
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [API & Web guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/api.md#headlessapiminimalapi)
