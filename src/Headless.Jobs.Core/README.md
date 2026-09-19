# Headless.Jobs.Core

Core implementation of the Jobs scheduler: in-memory persistence provider, execution task handler, background services, bounded task scheduler, and the `AddHeadlessJobs` DI extension.

## Why use this package

Provides reliable background job scheduling with cron expressions, delayed execution, custom task scheduling, retry logic, and bounded in-process execution without any external job scheduler dependencies (Hangfire, Quartz, etc.). The in-memory path works standalone; the durable path composes with `Headless.Jobs.EntityFramework`.

Stored requests may use GZip compression through `UseGZipCompression()`. Decompression is capped at 64 MiB by default; use `UseGZipCompression(maxDecompressedBytes)` when an application deliberately supports a different bounded payload size.

## Install

```bash
dotnet add package Headless.Jobs.Core
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Jobs (Background Jobs) guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/jobs.md#headlessjobscore)
