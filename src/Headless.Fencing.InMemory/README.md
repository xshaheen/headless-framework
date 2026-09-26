# Headless.Fencing.InMemory

Keeps fenced leases in process memory.

## Why use this package

Runs `IFencedLeases` and `unit.Leases` without a database, for tests, local development, and single-instance hosts. Leases coordinate the callers of one process only and disappear when it stops, so use a relational provider for work that several processes share.

## Install

```bash
dotnet add package Headless.Fencing.InMemory
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Fencing guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/fencing.md#headlessfencinginmemory)
