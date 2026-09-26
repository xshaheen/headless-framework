# Headless.Idempotency.SqlServer

Stores durable idempotency records in SQL Server.

## Why use this package

Keeps each idempotency key's record (fingerprint, admitted attempt, result, and retention) in a SQL Server table that is locked and written in the same transaction as the key's fenced lease, so concurrent admissions of one key serialize without unique-key errors, retention is decided by the database clock, and a stale attempt can never store its result.

## Install

```bash
dotnet add package Headless.Idempotency.SqlServer
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Idempotency guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/idempotency.md#headlessidempotencysqlserver)
