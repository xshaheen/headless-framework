# Headless.Security

Default implementations of `IStringEncryptionService` and `IStringHashService`, plus idempotent DI registration helpers.

Contracts, options, implementations, and registration extensions all use the `Headless.Security` namespace.

## Why use this package

Ships the concrete AES-GCM encryption and PBKDF2 hashing implementations so application code depends only on the `Headless.Security.Abstractions` contracts. Keeps security concerns separate from `Headless.Core` and `Headless.Api`.

## Install

```bash
dotnet add package Headless.Security
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Core guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/core.md#headlesssecurity)
