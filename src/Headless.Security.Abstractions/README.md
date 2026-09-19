# Headless.Security.Abstractions

Security contracts and option models for string encryption and hashing — no implementation, no DI coupling.

All public contracts and options use the `Headless.Security` namespace.

## Why use this package

Allows downstream packages and application layers to depend on encryption and hashing abstractions without referencing a concrete implementation. `Headless.Settings.Core` depends on `IStringEncryptionService` from this package; consuming code can swap the implementation independently.

## Install

```bash
dotnet add package Headless.Security.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Core guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/core.md#headlesssecurityabstractions)
