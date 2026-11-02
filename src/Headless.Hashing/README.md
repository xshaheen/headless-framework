# Headless.Hashing

The default `ISecretHasher` with PBKDF2-SHA256 built in, plus its registration.

Contracts, options, implementations, and registration extensions all use the `Headless.Hashing` namespace.

## Why use this package

Ships verify-capable secret hashing (PBKDF2-SHA256 built in, Argon2id through `Headless.Hashing.Argon2`) so application code depends only on the `Headless.Hashing.Abstractions` contracts.

## Install

```bash
dotnet add package Headless.Hashing
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Hashing guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/hashing.md#headlesshashing)
