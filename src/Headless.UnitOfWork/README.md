# Headless.UnitOfWork

Singleton unit-of-work factory and engine for explicit transactional boundaries.

## Why use this package

Implements the singleton `UnitOfWorkFactory` (independent units, nothing ambient or scoped, no `AsyncLocal`), the in-process unit engine with the atomic terminal claim, and `AddUnitOfWork()`.

## Install

```bash
dotnet add package Headless.UnitOfWork
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Unit of Work guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/unit-of-work.md#headlessunitofwork)
