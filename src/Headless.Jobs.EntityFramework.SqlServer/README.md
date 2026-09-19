# Headless.Jobs.EntityFramework.SqlServer

SQL Server atomic-claim provider for the Headless Jobs EF store.

## Why use this package

Replaces the portable EF select-and-compare-and-swap pickup path with SQL Server-native atomic claim-and-output operations under scheduler contention.

This package composes `Headless.Jobs.EntityFramework` with SQL Server claims and an application DbContext setup path. EF continues to own job storage, mapping definitions, recovery, the public persistence contract, and transaction-lifecycle primitives; this package owns SQL Server-specific claim execution, including SQL, parameters, and locking behavior.

## Install

```bash
dotnet add package Headless.Jobs.EntityFramework.SqlServer
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Jobs (Background Jobs) guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/jobs.md#headlessjobsentityframeworksqlserver)
