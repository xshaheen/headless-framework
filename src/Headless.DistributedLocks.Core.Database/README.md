# Headless.DistributedLocks.Core.Database

Shared relational lock engine used by the first-party database lock providers.

## Why use this package

Lets database providers map session-scoped or transaction-scoped lock primitives onto the standard distributed-lock abstractions without adding ADO.NET-specific machinery to Redis or cache providers.

## Install

```bash
dotnet add package Headless.DistributedLocks.Core.Database
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Distributed Locks guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/distributed-locks.md#headlessdistributedlockscoredatabase)
