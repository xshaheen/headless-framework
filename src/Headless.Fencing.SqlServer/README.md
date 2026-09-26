# Headless.Fencing.SqlServer

Stores fenced leases in SQL Server.

## Why use this package

Grants leases whose expiry is decided by the database clock and whose generations come from one store-wide sequence, fences a stale attempt's writes inside the caller's SQL Server unit-of-work transaction, and sweeps expired leases with `READPAST` so concurrent sweepers hand each one to exactly one handler, with or without read committed snapshot isolation.

## Install

```bash
dotnet add package Headless.Fencing.SqlServer
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Fencing guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/fencing.md#headlessfencingsqlserver)
