# Headless.Search.PostgreSql

Builds Arabic search keys in PostgreSQL, with the same rules as the C# normalizer.

## Why use this package

Ships the immutable `ar_norm_v1` function as a schema-runner step, with no EF Core dependency, so SQL or Dapper code can build and query Arabic search keys: `احمد` finds `أحمد` and `فاطمه` finds `فاطمة`. EF Core apps add `Headless.Search.PostgreSql.EntityFramework` for the model helpers.

## Install

```bash
dotnet add package Headless.Search.PostgreSql
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Search guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/search.md#headlesssearchpostgresql)
