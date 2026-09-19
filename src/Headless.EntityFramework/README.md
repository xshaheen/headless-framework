# Headless.EntityFramework

Entity Framework Core integration with framework conventions and save pipeline orchestration.

## Why use this package

Provides a framework-aware base `DbContext` with conventions for audit fields, EF model-driven audit-log capture, soft delete, tenant filters, two-tier event dispatch (in-process domain events plus transactional integration-event outbox), and transaction-aware save behavior — so application contexts inherit a consistent, tested baseline without hand-wiring each concern.

## Install

```bash
dotnet add package Headless.EntityFramework
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [ORM guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/orm.md#headlessentityframework)
