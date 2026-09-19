# Headless.Caching.Bcl

Adapter that exposes a named Headless cache as `Microsoft.Extensions.Caching.Distributed.IDistributedCache`.

## Why use this package

Provides standard BCL distributed-cache interop for ASP.NET Core Session and third-party libraries that require `IDistributedCache`, while keeping application code on the richer Headless `ICache` API.

## Install

```bash
dotnet add package Headless.Caching.Bcl
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Caching guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/caching.md#headlesscachingbcl)
