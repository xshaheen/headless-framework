# Headless.Fencing.PostgreSql

Stores fenced leases in PostgreSQL.

## Why use this package

Grants leases whose expiry is decided by the database clock and whose generations come from one store-wide sequence, fences a stale attempt's writes inside the caller's PostgreSQL unit-of-work transaction, and sweeps expired leases with `SKIP LOCKED` so concurrent sweepers hand each one to exactly one handler.

## Install

```bash
dotnet add package Headless.Fencing.PostgreSql
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Fencing guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/fencing.md#headlessfencingpostgresql)
