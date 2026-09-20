# Headless.UnitOfWork.Abstractions

Provider-neutral contracts for explicit units of work.

## Why use this package

Defines the public unit-of-work contracts without provider dependencies: the scoped manager entry point, the unit handle, the resource seams, the `IUnitOfWorkFeatureProvider` seam bridge packages attach capabilities through, and the `TransactionEnlistment` knob Jobs schedules with.

## Install

```bash
dotnet add package Headless.UnitOfWork.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Unit of Work guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/unit-of-work.md#headlessunitofworkabstractions)
