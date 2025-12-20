# headless-framework — Developer Overview

This document gives a high-level, developer-friendly view of **headless-framework**: what it is, how the repo is organized, and how to pick and wire the right NuGet packages.

## What this repo is

**headless-framework** is a modular set of NuGet packages for building APIs and backend services in .NET.

-   You compose capabilities by installing a few packages instead of adopting a monolith.
-   Most packages in this repo target `net10.0`.
-   Public APIs are designed for explicit wiring: you opt in to features via registration/extension methods.

## How to get value quickly

1. **Choose the abstraction first**, then add exactly one provider implementation.

    - Example pattern:
        - `Framework.Caching.Abstraction` + one provider (e.g., `Framework.Caching.Foundatio.Redis`)
        - `Framework.Blobs.Abstraction` + one provider (e.g., `Framework.Blobs.Azure`)

2. **Keep packages small and intentional**: add only what you need.

## Typical “stack” compositions

These are common combinations (not requirements):

### API

-   `Framework.Api` — core API building blocks
-   `Framework.Api.MinimalApi` or `Framework.Api.Mvc` — hosting style
-   `Framework.Api.FluentValidation` — request validation
-   `Framework.Api.Logging.Serilog` — structured logging helpers

Primary entrypoints:

-   [src/Framework.Api/ApiRegistration.cs](../src/Framework.Api/ApiRegistration.cs)
-   [src/Framework.Api.MinimalApi/AddMinimalApiExtensions.cs](../src/Framework.Api.MinimalApi/AddMinimalApiExtensions.cs)
-   [src/Framework.Api.Mvc/AddMvcExtensions.cs](../src/Framework.Api.Mvc/AddMvcExtensions.cs)

### OpenAPI

-   `Framework.OpenApi.Nswag` (and optionally `Framework.OpenApi.Nswag.OData`)

Primary entrypoint:

-   [src/Framework.OpenApi.Nswag/AddNswagSwaggerExtensions.cs](../src/Framework.OpenApi.Nswag/AddNswagSwaggerExtensions.cs)

### Data access

-   ORM integrations:
    -   `Framework.Orm.EntityFramework`
    -   `Framework.Orm.Dapper`
    -   `Framework.Orm.Couchbase`
-   SQL provider packages:
    -   `Framework.Sql.SqlServer`
    -   `Framework.Sql.PostgreSql`
    -   `Framework.Sql.Sqlite`

### Caching

-   `Framework.Caching.Abstraction`
-   Providers:
    -   `Framework.Caching.Foundatio.Memory`
    -   `Framework.Caching.Foundatio.Redis`

### Messaging / queueing

-   `Framework.Messaging.Abstractions`
-   Implementations:
    -   `Framework.Messaging.Cap`
    -   `Framework.Messaging.Foundatio`
    -   `Framework.Messaging.LocalServiceProvider`
-   Queueing:
    -   `Framework.Queueing.Abstraction`
    -   `Framework.Queueing.Foundatio`

### Files, blobs, uploads

-   `Framework.Blobs.Abstraction`
-   Providers:
    -   `Framework.Blobs.Azure`, `Framework.Blobs.Aws`, `Framework.Blobs.FileSystem`, `Framework.Blobs.Redis`, `Framework.Blobs.SshNet`
-   Resumable uploads:
    -   `Framework.Tus`, plus providers/integrations under `Framework.Tus.*`

AI context files:

-   [llms.txt](../llms.txt) — compact, consumer-focused index
-   [llms-full.txt](../llms-full.txt) — expanded index
