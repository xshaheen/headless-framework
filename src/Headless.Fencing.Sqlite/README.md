# Headless.Fencing.Sqlite

Stores fenced leases in SQLite.

## Why use this package

Grants leases whose expiry is decided by the database clock and whose generations come from one store-wide sequence, fences a stale attempt's writes inside the caller's SQLite unit-of-work transaction, and sweeps expired leases so concurrent sweepers hand each one to exactly one handler.

Limit: a lease cannot be granted inside a caller's unit of work (`unit.Leases.GrantAsync` throws `NotSupportedException`). SQLite has no counter that survives the caller's rollback, so a rolled-back grant's generation could be issued again as another holder's fencing token. Grant autonomously; the unit can still renew, settle, release, or fence the lease.

## Install

```bash
dotnet add package Headless.Fencing.Sqlite
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Fencing guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/fencing.md#headlessfencingsqlite)
