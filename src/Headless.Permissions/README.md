# Headless.Permissions

Core implementation of permission management with grant resolution, caching, background initialization, and ASP.NET Core authorization integration.

## Why use this package

Provides the full permission management runtime: AWS IAM-style grant resolution (User > Role), grant caching with cross-process invalidation, background startup sync of static definitions, and `PermissionRequirement` / `PermissionsRequirement` for wiring into ASP.NET Core authorization policies.

## Install

```bash
dotnet add package Headless.Permissions
```

## Column lengths

Every string column's maximum length is an option on `PermissionsStorageOptions` (for example `PermissionGrantsProviderKeyMaxLength`, default 64), set through `setup.ConfigureStorage(...)`. The defaults match the schema earlier versions created. The option sizes new tables only: to change a length after the tables exist, add an EF Core migration (EF storage) or run the `ALTER TABLE ... ALTER COLUMN` yourself (raw PostgreSQL or SQL Server storage), together with the option change. The [column lengths section](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/permissions.md#column-lengths) lists every option and both paths.

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Permissions guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/permissions.md#headlesspermissions)
