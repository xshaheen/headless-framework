# Headless.Security.Abstractions

Security contracts and option models for lookup hashing, secret hashing, and password generation — no implementation, no DI coupling.

All public contracts and options use the `Headless.Security` namespace.

## Why use this package

Allows downstream packages and application layers to depend on hashing abstractions without referencing a concrete implementation. For values that must be read back, use `Headless.Encryption`.

## Install

```bash
dotnet add package Headless.Security.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Security guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/security.md#headlesssecurityabstractions)
