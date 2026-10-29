# Headless.Search.PostgreSql

Builds Arabic search keys in PostgreSQL, with the same rules as the C# normalizer.

## Why use this package

Ships the immutable `ar_norm_v1` function as a schema-runner step, and EF Core helpers that store a trigram-indexed search key beside each searchable name and normalize query terms in SQL, so `احمد` finds `أحمد` and `فاطمه` finds `فاطمة` through an index.

## Install

```bash
dotnet add package Headless.Search.PostgreSql
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Search guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/search.md#headlesssearchpostgresql)
