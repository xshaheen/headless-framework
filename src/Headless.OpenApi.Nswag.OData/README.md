# Headless.OpenApi.Nswag.OData

NSwag operation filter that injects OData query parameters into the OpenAPI spec for endpoints that support OData queries.

## Why use this package

When ASP.NET Core OData endpoints accept `ODataQueryOptions` or carry `[EnableQuery]`, NSwag does not automatically document the OData query string parameters. This package detects those endpoints and injects the seven standard OData parameters into their OpenAPI operation objects.

## Install

```bash
dotnet add package Headless.OpenApi.Nswag.OData
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [OpenAPI guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/openapi.md#headlessopenapinswagodata)
