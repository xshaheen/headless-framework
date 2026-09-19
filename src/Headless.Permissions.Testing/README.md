# Headless.Permissions.Testing

Test-only doubles that bypass all permission and authorization checks.

## Why use this package

Integration tests often need to exercise endpoints without wiring up real grants. This package supplies always-allow replacements for `IPermissionManager` and `IAuthorizationService`, kept out of `Headless.Permissions.Core` so the production surface never ships an authorization bypass.

## Install

```bash
dotnet add package Headless.Permissions.Testing
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Permissions guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/permissions.md#headlesspermissionstesting)
