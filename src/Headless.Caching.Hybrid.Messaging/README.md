# Headless.Caching.Hybrid.Messaging

Backplane package that sends hybrid-cache L1 invalidations to every host over the Headless messaging bus.

## Why use this package

`Headless.Caching.Hybrid` defines the `IHybridCacheBackplane` seam and refuses to start until the host chooses a backplane, but it carries no messaging dependency. This package supplies the messaging backplane: each write publishes a `CacheInvalidationMessage`, and an every-instance consumer on each host evicts the stale L1 entries. Use it when the hosts already share a Headless messaging transport; use `UseRedisBackplane()` from `Headless.Caching.Redis` when they share Redis instead.

## Install

```bash
dotnet add package Headless.Caching.Hybrid.Messaging
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Caching guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/caching.md#headlesscachinghybridmessaging)
