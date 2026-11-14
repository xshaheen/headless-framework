# Headless.Features

Core implementation of feature management with caching, value providers, and definition management.

## Why use this package

Provides the full feature management implementation including hierarchical value resolution (Tenant > Edition > Default), feature value caching, background initialization that seeds static definitions into the database, and an extensible value-provider pipeline.

## Install

```bash
dotnet add package Headless.Features
```

## Column lengths

Every string column's maximum length is an option on `FeaturesStorageOptions` (for example `FeatureValuesProviderKeyMaxLength`, default 64), set through `setup.ConfigureStorage(...)`. The defaults match the schema earlier versions created. The option sizes new tables only: to change a length after the tables exist, add an EF Core migration (EF storage) or run the `ALTER TABLE ... ALTER COLUMN` yourself (raw PostgreSQL or SQL Server storage), together with the option change. The [column lengths section](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/features.md#column-lengths) lists every option and both paths.

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Feature Management guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/features.md#headlessfeatures)
