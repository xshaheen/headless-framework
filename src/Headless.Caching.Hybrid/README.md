# Headless.Caching.Hybrid

Two-tier cache combining in-memory L1 with remote L2 and cross-instance invalidation through a pluggable backplane.

## Why use this package

Provides one `ICache` implementation that reads from a fast local cache first, falls back to a shared remote cache, and invalidates other instances when writes change cached data. The invalidations travel over a backplane the host chooses: Redis pub/sub (`Headless.Caching.Redis`), the Headless messaging bus (`Headless.Caching.Hybrid.Messaging`), or none for a single-instance host.

## Install

```bash
dotnet add package Headless.Caching.Hybrid
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Caching guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/caching.md#headlesscachinghybrid)
