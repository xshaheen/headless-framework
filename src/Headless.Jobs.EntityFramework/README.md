# Headless.Jobs.EntityFramework

Entity Framework Core persistence provider for `Headless.Jobs` — durable, distributed, multi-node job storage with database-clock lease authority.

## Why use this package

Provides persistence of time jobs and cron occurrences across restarts and across multiple nodes, using EF Core-mapped tables. Integrates with `Headless.Coordination` for distributed node identity (`node@incarnation`), dead-node recovery, and fail-stop on membership loss.

## Install

```bash
dotnet add package Headless.Jobs.EntityFramework
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Jobs (Background Jobs) guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/jobs.md#headlessjobsentityframework)
