# Headless.Domain.LocalEventBus

DI-based implementation of `IDomainEventDispatcher` for in-process domain event handling.

## Why use this package

Provides in-memory domain event dispatch that resolves handlers from the DI container, enabling decoupled event-driven architecture within a single process and unit of work.

## Install

```bash
dotnet add package Headless.Domain.LocalEventBus
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Core guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/core.md#headlessdomainlocaleventbus)
