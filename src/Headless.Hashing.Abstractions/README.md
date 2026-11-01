# Headless.Hashing.Abstractions

Secret-hashing contracts and option models — no implementation, no DI coupling.

All public contracts and options use the `Headless.Hashing` namespace.

## Why use this package

Allows downstream packages and application layers to depend on `ISecretHasher` without referencing a concrete implementation. For values that must be read back or looked up, use `Headless.Encryption`.

## Install

```bash
dotnet add package Headless.Hashing.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Hashing guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/hashing.md#headlesshashingabstractions)
