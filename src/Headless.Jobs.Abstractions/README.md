# Headless.Jobs.Abstractions

Contracts, entity types, manager interfaces, and execution primitives for the Jobs system.

## Why use this package

Provides the shared contracts — `IJobScheduler`, `ITimeJobManager<TTimeJob>`, `ICronJobManager<TCronJob>`, descriptors, entity types, options, enums, exception types, and execution context — that decouple job enqueueing code from any specific Jobs persistence provider or scheduler implementation. All consumer code can target these abstractions without referencing `Headless.Jobs.EntityFramework`.

## Install

```bash
dotnet add package Headless.Jobs.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Jobs (Background Jobs) guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/jobs.md#headlessjobsabstractions)
