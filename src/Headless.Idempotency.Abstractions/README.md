# Headless.Idempotency.Abstractions

Defines the durable idempotency contracts: `IIdempotentOperations`, `IdempotentAdmission`, `IdempotencyFingerprint`, the stored `IdempotentResult`, and the `unit.Idempotency` accessor.

## Why use this package

Lets application code admit a keyed operation once across processes, replay its stored result on retry, and fence the operation's writes against a stale attempt, without referencing a database provider.

## Install

```bash
dotnet add package Headless.Idempotency.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Idempotency guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/idempotency.md#headlessidempotencyabstractions)
