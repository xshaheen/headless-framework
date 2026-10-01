# Headless.Jobs.SourceGenerator

Roslyn incremental source generator that eliminates reflection and manual job registration for the Jobs scheduler.

## Why use this package

Without the source generator, every job would have to be registered with the Jobs runtime by hand at startup. The source generator finds `[Job]` classes and Jobs middleware at compile time and emits one `JobsModule` per assembly, with a typed invoker for each job; the host registers it with `AddModule<JobsModule>()`, with no runtime assembly scanning.

## Install

```bash
dotnet add package Headless.Jobs.SourceGenerator
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Jobs (Background Jobs) guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/jobs.md#headlessjobssourcegenerator)
