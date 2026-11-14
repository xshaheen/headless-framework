# Headless.Settings

Core implementation of dynamic settings management with hierarchical value providers, caching, encryption, and background initialization.

## Why use this package

Provides the full settings management implementation including hierarchical value resolution (User > Tenant > Global > Configuration > DefaultValue), setting value caching with distributed invalidation, transparent encryption for sensitive settings, and background startup initialization that seeds static definitions to the database.

## Install

```bash
dotnet add package Headless.Settings
```

## Column lengths

Every string column's maximum length is an option on `SettingsStorageOptions` (for example `SettingValuesProviderKeyMaxLength`, default 64), set through `setup.ConfigureStorage(...)`. The defaults match the schema earlier versions created. The option sizes new tables only: to change a length after the tables exist, add an EF Core migration (EF storage) or run the `ALTER TABLE ... ALTER COLUMN` yourself (raw PostgreSQL or SQL Server storage), together with the option change. The [column lengths section](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/settings.md#column-lengths) lists every option and both paths.

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Settings guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/settings.md#headlesssettings)
