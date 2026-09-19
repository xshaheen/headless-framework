# Headless.Api.DataProtection

Extends ASP.NET Core Data Protection to persist encryption keys to blob storage providers.

## Why use this package

In distributed/containerized environments, ASP.NET Core Data Protection keys must be shared across instances. This package enables key persistence to any `IBlobStorage` implementation (Azure, AWS S3, local filesystem, etc.).

## Install

```bash
dotnet add package Headless.Api.DataProtection
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [API & Web guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/api.md#headlessapidataprotection)
