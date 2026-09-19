# Headless.DistributedLocks.PostgreSql

PostgreSQL advisory-lock provider for cross-node coordination.

## Why use this package

Coordinates work across nodes using PostgreSQL advisory locks, with no Redis dependency and with transaction-coupled locking available for data mutations already protected by a PostgreSQL transaction.

## Install

```bash
dotnet add package Headless.DistributedLocks.PostgreSql
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Distributed Locks guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/distributed-locks.md#headlessdistributedlockspostgresql)
