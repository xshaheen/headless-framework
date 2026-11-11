# Headless.LiveQueries

Runs the in-process live-query hub: a watched query runs again when a dependency it recorded is announced as changed, and yields each new result.

## Why use this package

Provides `AddHeadlessLiveQueries`, the hub behind `ILiveQueries`, `ILiveQueryInvalidator` and `ILiveQueryDependencies`, and the `Headless.LiveQueries` meter. Invalidation reaches subscriptions in the same process only; read the guide before running more than one instance.

## Install

```bash
dotnet add package Headless.LiveQueries
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Live Queries guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/live-queries.md#headlesslivequeries)
