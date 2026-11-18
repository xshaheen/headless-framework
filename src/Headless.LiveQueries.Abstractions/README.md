# Headless.LiveQueries.Abstractions

Defines the live-query contracts: `ILiveQueries`, `ILiveQueryInvalidator`, `ILiveQueryDependencies`, and the `LiveUpdate` a transport forwards.

## Why use this package

Lets application code watch a query, declare what it depends on, and announce a change, and lets a transport author carry updates, without referencing ASP.NET Core, Entity Framework Core, or the hub.

## Install

```bash
dotnet add package Headless.LiveQueries.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Live Queries guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/live-queries.md#headlesslivequeriesabstractions)
