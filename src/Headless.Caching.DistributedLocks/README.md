# Headless.Caching.DistributedLocks

Adapter that bridges the caching factory-lock seam (`ICacheFactoryLockProvider`) onto `IDistributedLock`, enabling opt-in multi-node cache stampede protection for entries that set `CacheEntryOptions.UseDistributedFactoryLock`.

## Why use this package

The per-key factory lock in `Headless.Caching.Core` is process-local: with N app instances sharing one Redis cache, a popular key expiring can still run N concurrent factories — one per node. This package makes the factory single-flight across nodes: the node that wins a distributed lock runs the factory, the others wait on the lock and re-check the shared store, so the losers serve the winner's freshly written value instead of duplicating the work.

## Install

```bash
dotnet add package Headless.Caching.DistributedLocks
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Caching guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/caching.md#headlesscachingdistributedlocks)
