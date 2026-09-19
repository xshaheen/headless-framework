# Headless.Couchbase

Couchbase integration with bucket context, document set operations, cluster management, and transaction support.

## Why use this package

Provides a typed context model over Couchbase buckets with helper APIs for document operations (KV, LookupIn, MutateIn, scan, N1QL queries, transactions) and schema bootstrap (scope/collection/index lifecycle), following the same context-provider pattern as `Headless.EntityFramework` but for the document model.

## Install

```bash
dotnet add package Headless.Couchbase
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [ORM guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/orm.md#headlesscouchbase)
