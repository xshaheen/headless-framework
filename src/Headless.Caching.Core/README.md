# Headless.Caching.Core

Factory-backed cache orchestration shared by cache providers.

`FactoryCacheCoordinator` owns the common `GetOrAddAsync` state machine: read, keyed lock, double-check, factory execution, fail-safe stale serving, and throttle restamping. Providers implement `IFactoryCacheStore` so the same resilience behavior applies across memory, Redis, and hybrid caches without duplicating the orchestration flow.
