# Headless.Caching.OutputCache

Adapter that backs ASP.NET Core's `IOutputCacheStore` with a named Headless `ICache`, making `services.AddOutputCache()` distributed and tag-aware.

## Why use this package

ASP.NET Core's output-cache middleware ships only an in-memory store; its own guidance states that `IDistributedCache` is **not** a valid output-cache store because it lacks the atomic tag operations the middleware needs for `EvictByTagAsync`. This package fills that gap: it backs an `IOutputCacheStore` (and the optional `IOutputCacheBufferStore`) with the Headless cache engine, so output-cache entries become distributed and tag eviction rides the engine's distributed tag index — without forcing an ASP.NET dependency onto framework-agnostic `Headless.Caching.Bcl` consumers.

## Install

```bash
dotnet add package Headless.Caching.OutputCache
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Caching guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/caching.md#headlesscachingoutputcache)
