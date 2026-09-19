# Headless.Caching.Hybrid

Two-tier cache combining in-memory L1 with remote L2 and cross-instance invalidation through messaging.

## Why use this package

Provides one `ICache` implementation that reads from a fast local cache first, falls back to a shared remote cache, and invalidates other instances when writes change cached data.

## Install

```bash
dotnet add package Headless.Caching.Hybrid
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Caching guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/caching.md#headlesscachinghybrid)
