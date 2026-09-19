# Headless.Jobs.SourceGenerator

Roslyn incremental source generator that eliminates reflection and manual job registration for the Jobs scheduler.

## Why use this package

Without the source generator, every job class or method must be manually registered with the Jobs runtime at startup, and job dispatch uses reflection to invoke methods. The source generator scans for `[JobFunction]` attributes at compile time and emits a module initializer that auto-registers all discovered jobs before `Main` runs, with zero reflection at runtime.

## Install

```bash
dotnet add package Headless.Jobs.SourceGenerator
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Jobs (Background Jobs) guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/jobs.md#headlessjobssourcegenerator)
