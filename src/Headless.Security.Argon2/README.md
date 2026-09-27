# Headless.Security.Argon2

Argon2id for `ISecretHasher`, selected with `UseArgon2id()` on the secret-hasher setup builder.

## Why use this package

Argon2id is the current recommendation for storing secrets such as PINs, API-key secrets, and recovery codes, and .NET has no built-in implementation. This package supplies it through libsodium (via NSec), which ships native binaries for Windows, Linux (glibc and musl), and macOS. It is a separate package so that applications using only PBKDF2, or none of the hashing features, do not carry a native dependency.

## Install

```bash
dotnet add package Headless.Security.Argon2
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Security guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/security.md)
