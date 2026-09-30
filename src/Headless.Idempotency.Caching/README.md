# Headless.Idempotency.Caching

Keeps durable idempotency records in the application's `ICache`.

## Why use this package

Runs `IIdempotentOperations` and the HTTP idempotency middleware across several replicas that share a Redis cache, without a SQL database. Each record is one cache entry changed by compare-and-swap, so concurrent admissions still admit exactly one attempt. The guarantees are weaker than a relational provider's: records last only as long as the cache keeps them, leases run on the application clock, and `unit.Idempotency` is not supported, because a cache cannot commit or roll back with a unit of work.

## Install

```bash
dotnet add package Headless.Idempotency.Caching
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Idempotency guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/idempotency.md#headlessidempotencycaching)
