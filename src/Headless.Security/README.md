# Headless.Security

Default implementations of `IStringEncryptionService`, `ILookupHasher`, and `ISecretHasher`, plus idempotent DI registration helpers.

Contracts, options, implementations, and registration extensions all use the `Headless.Security` namespace.

## Why use this package

Ships the concrete AES-GCM encryption, PBKDF2 lookup hashing, and verify-capable secret hashing (PBKDF2-SHA256 built in, Argon2id through `Headless.Security.Argon2`) so application code depends only on the `Headless.Security.Abstractions` contracts. Keeps security concerns separate from `Headless.Core` and `Headless.Api`.

## Install

```bash
dotnet add package Headless.Security
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Security guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/security.md#headlesssecurity)
