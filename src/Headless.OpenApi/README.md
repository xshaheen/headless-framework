# Headless.OpenApi

Headless enrichment for ASP.NET Core OpenAPI documents: typed problem details, authorization responses, security schemes, FluentValidation constraints, primitive mappings, and stable operation ids.

## Why use this package

The native `Microsoft.AspNetCore.OpenApi` generator leaves gaps that matter to generated clients: numbers read from strings become `integer | string` unions, non-nullable properties are not required, controller actions have no operation id, and error responses carry an untyped `ProblemDetails`. This package closes them with document, operation, and schema transformers, applied by one `AddHeadlessOpenApi()` call or by `AddHeadless()`.

## Install

```bash
dotnet add package Headless.OpenApi
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [OpenAPI guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/openapi.md#headlessopenapi)
