# Headless.Caching.Redis

Redis distributed cache implementation for multi-instance applications.

## Why use this package

Provides Redis-backed caching through the unified `ICache` abstraction, enabling cache sharing across multiple application instances. It also supplies a Redis pub/sub backplane for `Headless.Caching.Hybrid`, so hosts that share Redis evict each other's stale L1 entries without a message broker.

## Install

```bash
dotnet add package Headless.Caching.Redis
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Caching guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/caching.md#headlesscachingredis)
