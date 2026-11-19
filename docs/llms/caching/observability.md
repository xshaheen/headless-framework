---
domain: Caching
packages:
---

# Observability

> Part of [Caching](../caching.md). The OpenTelemetry metrics and traces the cache emits, and the in-process `cache.Events` hooks.

## Register the instrumentation

Every cache emits metrics and traces under one name, `Headless.Caching`, used for both the `Meter` and the `ActivitySource` and exposed as `CachingDiagnostics.SourceName`. Register it either way:

- `tracing.AddCachingInstrumentation()` and `metrics.AddCachingInstrumentation()`. They live in the `OpenTelemetry.Trace` and `OpenTelemetry.Metrics` namespaces and need only `OpenTelemetry.Api`.
- `AddSource(CachingDiagnostics.SourceName)` and `AddMeter(CachingDiagnostics.SourceName)`.

With no listener attached, the cache builds no spans and no tag lists, so an unobserved cache pays no instrumentation cost.

**Verify:** after a `GetOrAddAsync` call, the trace exporter shows a `cache.get_or_add` span, and the metrics exporter shows `headless.cache.requests` with `headless.cache.operation = get_or_add`.

## Dimensions and privacy

- Every instrument carries `headless.cache.name`: the instance name, or `default` for the unnamed cache. The provider options' `CacheName` sets it; named instances set it for you.
- The raw cache key is never a metric dimension. It appears on spans only when you opt in with `AddHeadlessCaching(setup => setup.IncludeKeyInTraces = true)`. It is off by default because keys often carry tenant or user identifiers.

## Metrics

| Instrument | Kind | Unit | Meaning | Dimensions besides `headless.cache.name` |
| --- | --- | --- | --- | --- |
| `headless.cache.requests` | Counter (`long`) | count | Read outcomes. Operations: `get_or_add`, and the direct reads `get`, `get_all` (one hit add and one miss add per call), and `exists`. | `headless.cache.operation`, `headless.cache.outcome` (`hit`, `miss`, `stale`), `headless.cache.tier` (`l1`, `l2`, `hybrid`) |
| `headless.cache.writes` | Counter (`long`) | count | Upserts and removes: `upsert`, `remove`, `remove_by_prefix`, `remove_by_tag`. A remove counts the removed keys where that is cheap to know, else 1 per call. | `headless.cache.operation`, `headless.cache.tier` |
| `headless.cache.evictions` | Counter (`long`) | count | Evictions, in-memory tier only; Redis evicts on the server, out of the client's sight. | `headless.cache.evict_reason` (`expired`, `capacity`, `removed`, `flushed`), `headless.cache.tier` |
| `headless.cache.factory.executions` | Counter (`long`) | count | Factory runs; the denominator of a failure rate. | `headless.cache.outcome` (`success`, `error`, `timeout`) |
| `headless.cache.factory.duration` | Histogram (`double`) | milliseconds | Factory latency. | `headless.cache.outcome` |
| `headless.cache.failsafe.activations` | Counter (`long`) | count | Stale values served by fail-safe. | `headless.cache.trigger` (`factory_error`, `factory_timeout`, `lock_acquire_failed`) |
| `headless.cache.refreshes` | Counter (`long`) | count | Eager and background refreshes. | `headless.cache.refresh_kind` (`eager`, `background`), `headless.cache.outcome` (`success`, `error`) |
| `headless.cache.invalidations` | Counter (`long`) | count | Hybrid tag, clear, and flush propagation. | `headless.cache.invalidation_kind` (`tag`, `clear`, `flush`), `headless.cache.direction` (`publish`, `receive`) |
| `headless.cache.events.dropped` | Counter (`long`) | count | Event signals dropped because the event buffer was full or shutting down. | none |

How to read the counts without double counting:

- `GetOrAddAsync` records its outcome, factory, fail-safe, and refresh signals once, for every provider. Its `requests` use `tier = hybrid` on a hybrid cache.
- Direct `ICache` operations record at each provider with tier `l1` or `l2`, so cache-aside code that never calls `GetOrAddAsync` still shows read volume and hit rate.
- On a hybrid, a `GetOrAddAsync` that misses L1 and hits L2 also records `requests{outcome=miss,tier=l1}` and `requests{outcome=hit,tier=l2}`. Only the first read of each tier per call counts, so a cold miss records one `l1` miss and one `l2` miss, however often the call re-reads while it waits for a lock.
- A hybrid's direct reads (not `GetOrAddAsync`) record their L1 probe as `get` or `get_all` under the L1 instance's own `headless.cache.name`. Their L2 fallback records nothing.

## Traces

`GetOrAddAsync` starts a `cache.get_or_add` span with `headless.cache.name`, `headless.cache.tier`, and the resolved `headless.cache.outcome`. The factory runs as a child `cache.factory` span, and a distributed-lock acquire as a child `cache.factory_lock` span.

- A failure the caller sees (a hard timeout with no fallback, or a propagated exception) sets the span status to `Error`.
- A fail-safe activation is not an error. It adds a `cache.failsafe` span event with the trigger and the attribute `headless.cache.failsafe=true`.

## Events

Every cache exposes in-process hooks on `cache.Events`, for work such as warming a related key, invalidating a downstream resource, or driving a UI counter. Each event is an `IAsyncEvent<TArgs>` (`Headless.Primitives`). Subscribe with `AddHandler`, and unsubscribe by disposing the returned registration. Handlers may be synchronous or asynchronous:

```csharp
using var sub = cache.Events.Hit.AddHandler(e => logger.LogDebug("hit {Key} stale={Stale}", e.Key, e.IsStale));

cache.Events.FailSafeActivation.AddHandler(async (e, ct) => await notifier.FailSafeAsync(e.Trigger, ct));
```

**The events.**

- From `GetOrAddAsync`: `Hit` (with `IsStale`), `Miss`, `Set`, `FactorySuccess`, `FactoryError`, `FactoryTimeout`, `FailSafeActivation` (with its trigger), `EagerRefresh`, and `BackgroundRefresh`.
- From direct operations: `Set`, `Remove`, `RemoveAll`, `RemoveByPrefix`, `RemoveByTag`, `Clear`, and `Flush`.
- In-memory only: `Eviction`, with its reason. It also fires when a read reaps an expired entry, which the `evictions` metric does not count.
- Hybrid only: `Invalidation` (kind `tag`, `clear`, or `flush`; direction `publish` or `receive`), and per-tier reads under `cache.Events.Memory` (L1) and `cache.Events.Distributed` (L2). Both are `null` on a single-tier cache. L1 evictions fire on the L1 cache's own `Events.Eviction`, not on the hybrid.

Every argument carries `CacheName` and `Tier`. Keyed events add `Key`, the key as the caller wrote it, without the provider's `KeyPrefix`. The enums are `CacheTier`, `CacheEvictionReason`, `CacheFailSafeTrigger`, `CacheFactoryOutcome`, `CacheRefreshKind`, `CacheInvalidationKind`, and `CacheInvalidationDirection`.

**Delivery.**

- Events are best-effort observability, not a durable channel. One bounded background queue per cache (shared by a hybrid and its tiers) runs signals one at a time, in emission order across event types. The operation that emitted a signal never waits for it.
- Each signal runs the handlers subscribed when it was emitted; a later subscribe or unsubscribe affects only later signals.
- A handler exception is caught and logged at `EventHandlerErrorLogLevel` (default `Warning`). It never reaches the cache caller and never stops other handlers.
- The queue holds `EventBufferCapacity` signals (default 2,048). When it is full, the new signal is dropped; an older accepted one is never displaced, and the cache operation never blocks. `cache.Events.DispatchStatistics` reports accepted, processed, dropped, and pending counts, each drop increments `headless.cache.events.dropped`, and the drop warning logs at power-of-two drop counts.
- Disposing the cache drains accepted signals for up to `EventShutdownDrainTimeout` (default 2 seconds), then abandons the rest.
- An event with no subscriber costs nothing: no arguments are built.
- `ScopedCache<T>`, which `AddTenantScopedCache<T>()` returns, exposes a no-op `Events`, because its shared inner cache would leak other scopes' events and prefixed keys. Subscribe on the underlying cache.
- An `ICache` you implement yourself gets a no-op `Events` from the interface's default implementation.
