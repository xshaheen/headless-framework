# Headless.Search.PostgreSql.EntityFramework

Maps Arabic search keys into an EF Core model on PostgreSQL.

## Why use this package

Declares a stored, trigram-indexed search key beside each searchable name, maps the query function so EF Core normalizes search terms in SQL with the same rules, and moves stored keys to a later rule version in a migration.

## Install

```bash
dotnet add package Headless.Search.PostgreSql.EntityFramework
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Search guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/search.md#headlesssearchpostgresqlentityframework)
