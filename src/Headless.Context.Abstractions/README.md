# Headless.Context.Abstractions

Defines the contract surface for the Headless ambient-context family: who is calling (`ICurrentUser`), the principal being switched (`ICurrentPrincipalAccessor`, `PrincipalContext`), the locale and time zone of the request, correlation and cancellation, and the process identity every subsystem stamps on shared state.

## Why use this package

Provides a dependency-free contract surface for ambient execution context so packages across the framework (Api, AuditLog, EntityFramework, Mediator, Permissions, Settings, Features, ...) can read who/where/when without pulling in an implementation package. Each contract ships with a dependency-free default (`NullCurrentUser`, `DefaultCurrentLocale`, `CurrentCultureCurrentLocale`, `LocalCurrentTimeZone`, `DefaultCancellationTokenProvider`) where one makes sense; implementations that need DI or logging live in `Headless.Context`.

## Install

```bash
dotnet add package Headless.Context.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Context guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/context.md#headlesscontextabstractions)
