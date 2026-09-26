# Headless.Idempotency.InMemory

Keeps durable idempotency records in process memory.

## Why use this package

Runs `IIdempotentOperations`, `unit.Idempotency`, and the HTTP idempotency middleware without a database, for tests, local development, and single-instance hosts. Records deduplicate only the requests one process handles and disappear when it stops, so use a relational provider when several instances serve the same keys.

## Install

```bash
dotnet add package Headless.Idempotency.InMemory
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Idempotency guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/idempotency.md#headlessidempotencyinmemory)
