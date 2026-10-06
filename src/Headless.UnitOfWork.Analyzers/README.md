# Headless.UnitOfWork.Analyzers

Roslyn analyzers and code fixes that flag a write made through an autonomous service while a unit of work is in scope.

## Why use this package

Holding an `IUnitOfWork` and still calling `IBus.PublishAsync`, `IJobScheduler`, or another injected service compiles and passes tests, but that write commits on its own and atomicity is lost silently. These analyzers report each such call and name the enlisted receiver on your unit, such as `unit.Outbox`, and the code fix rewrites the call where the swap is one-to-one. Every rule is a suggestion by default; raise it per ID in `.editorconfig`.

## Install

```bash
dotnet add package Headless.UnitOfWork.Analyzers
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Unit of Work guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/unit-of-work.md#headlessunitofworkanalyzers)
