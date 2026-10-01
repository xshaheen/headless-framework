# Headless.Idempotency.Sqlite

Stores durable idempotency records in SQLite.

## Why use this package

Keeps each idempotency key's record (fingerprint, admitted attempt, result, and retention) in a SQLite table whose rows carry their own lease and generation, so concurrent admissions of one key serialize without unique-key errors, retention is decided by the database clock, and a stale attempt can never store its result.

Limit: a key cannot be admitted inside a caller's unit of work (`unit.Idempotency.AdmitAsync` throws `NotSupportedException`). SQLite has no counter that survives the caller's rollback, so a rolled-back admission's generation could be issued again. Admit autonomously; the unit can still complete, release, fence, or set a recovery point.

## Install

```bash
dotnet add package Headless.Idempotency.Sqlite
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Idempotency guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/idempotency.md#headlessidempotencysqlite)
