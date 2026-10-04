# Headless.Context

Implementations and DI setup for the ambient-context contracts: principal-based current user, the `AsyncLocal` principal accessor with its thread fallback, activity-based correlation, and host identity registration.

## Why use this package

Registers the process-wide ambient context a Headless application runs with. `AddHeadlessHostIdentity()` wires the accessor that Coordination, Settings, Features, and Permissions all read for the origin of a change (`TryAdd`, so feature packages call it too and the host's own call wins). The principal implementations back `ICurrentUser` and `ICurrentPrincipalAccessor` for hosts that resolve the principal themselves instead of through the Api pipeline. The contracts live in `Headless.Context.Abstractions`.

## Install

```bash
dotnet add package Headless.Context
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Context guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/context.md#headlesscontext)
