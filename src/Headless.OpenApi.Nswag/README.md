# Headless.OpenApi.Nswag

NSwag OpenAPI document generation with framework processors, FluentValidation schema integration, security schemes, and primitive type mappings.

## Why use this package

Configuring NSwag from scratch requires wiring multiple schema and operation processors, handling nullable generics, reflecting FluentValidation rules into JSON Schema, and adding standard security/error response shapes — all in the correct order. This package does all of that behind a single `AddNswagOpenApi()` call.

## Install

```bash
dotnet add package Headless.OpenApi.Nswag
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [OpenAPI guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/openapi.md#headlessopenapinswag)
