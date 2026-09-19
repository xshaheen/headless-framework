# Headless.Settings.Core

Core implementation of dynamic settings management with hierarchical value providers, caching, encryption, and background initialization.

## Why use this package

Provides the full settings management implementation including hierarchical value resolution (User > Tenant > Global > Configuration > DefaultValue), setting value caching with distributed invalidation, transparent encryption for sensitive settings, and background startup initialization that seeds static definitions to the database.

## Install

```bash
dotnet add package Headless.Settings.Core
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Settings guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/settings.md#headlesssettingscore)
