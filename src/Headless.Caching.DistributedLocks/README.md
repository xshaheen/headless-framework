# Headless.Caching.DistributedLocks

Opt-in multi-node cache stampede protection: bridges the Headless caching factory-lock seam (`ICacheFactoryLockProvider`) onto `IDistributedLock`, so a cache entry that sets `CacheEntryOptions.UseDistributedFactoryLock` runs its factory on exactly one node while other nodes coordinate through the distributed lock and re-check the shared store. Register it with `services.AddCachingDistributedFactoryLock()` alongside any `Headless.DistributedLocks.*` provider.
