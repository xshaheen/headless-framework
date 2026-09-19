# Headless.DistributedLocks.InMemory

In-process storage and setup helpers for distributed-lock abstractions.

## Why use this package

Provides a no-infrastructure backend for code that depends on `IDistributedLock`, `IDistributedReadWriteLock`, or `IDistributedSemaphoreProvider` in tests, local development, and single-instance applications.

## Install

```bash
dotnet add package Headless.DistributedLocks.InMemory
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Distributed Locks guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/distributed-locks.md#headlessdistributedlocksinmemory)
