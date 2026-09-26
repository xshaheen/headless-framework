# Headless.Idempotency.PostgreSql

Stores durable idempotency records in PostgreSQL.

## Why use this package

Keeps each idempotency key's record (fingerprint, admitted attempt, result, and retention) in a PostgreSQL table whose rows carry their own lease and generation, so concurrent admissions of one key serialize without unique-key errors, retention is decided by the database clock, and a stale attempt can never store its result.

## Install

```bash
dotnet add package Headless.Idempotency.PostgreSql
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Idempotency guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/idempotency.md#headlessidempotencypostgresql)
