# Headless.UnitOfWork.EntityFramework

EF Core provider for Headless units of work.

## Why use this package

Gives a plain EF Core `DbContext` the three unit-of-work entry points it needs: an owned begin (`BeginAsync(db)` — one line opens the transaction, one verb commits it and drains everything enlisted inside), an observed enlist (`Enlist(db, transaction)` — for code that owns its own commit edge), and an execution-strategy-safe block (`RunAsync(db, …)`). Misuse is loud: beginning over an existing transaction or under a retrying strategy throws a message that names the remedy. No interceptor, no options configuration, no startup gate — the unit of work owns its commit edge, so nothing needs to observe EF's transaction events.

## Install

```bash
dotnet add package Headless.UnitOfWork.EntityFramework
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Unit of Work guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/unit-of-work.md#headlessunitofworkentityframework)
