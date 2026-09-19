# Headless.Permissions.Core

Core implementation of permission management with grant resolution, caching, background initialization, and ASP.NET Core authorization integration.

## Why use this package

Provides the full permission management runtime: AWS IAM-style grant resolution (User > Role), grant caching with cross-process invalidation, background startup sync of static definitions, and `PermissionRequirement` / `PermissionsRequirement` for wiring into ASP.NET Core authorization policies.

## Install

```bash
dotnet add package Headless.Permissions.Core
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Permissions guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/permissions.md#headlesspermissionscore)
