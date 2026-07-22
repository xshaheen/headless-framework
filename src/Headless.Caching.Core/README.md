# Headless.Caching.Core

Shared factory-backed cache orchestration for cache providers.

## Problem Solved

Centralizes the `GetOrAddAsync` state machine so memory, Redis, and hybrid providers share the same factory execution, keyed locking, fail-safe fallback, timeout, eager refresh, conditional refresh, and background completion behavior.

## Key Features

- `FactoryCacheCoordinator` - shared factory orchestration engine; both the simple value factory and the conditional `CacheFactoryContext<T>` factory run on one state machine with identical timeout, fail-safe, and refresh semantics.
- `IFactoryCacheStore` - provider primitive for metadata-aware entry reads (single-key `TryGetEntryAsync` and position-aligned bulk `TryGetAllEntriesAsync`, with optional read options followed by an optional trailing `CancellationToken`; the bulk read resolves the whole batch's clear/remove/tag invalidation markers in one prefetch for O(1) marker round-trips regardless of key count), conditional writes, and metadata-only sliding re-arm (`TryRearmSlidingAsync`). Factory writes derived from an existing physical entry carry the entry's opaque `ConcurrencyStamp`; stores return `false` when the live entry no longer matches, preventing a late factory from resurrecting a removed key or clobbering a concurrent writer. (Stamp collision-resistance is provider-specific: the Redis stamp is the fixed frame header, so same-key writes with identical options within one millisecond may compare equal — a narrow window, closed by enabling jitter; see #583.)
- `CacheStoreEntry<T>` - entry snapshot with logical, physical, and sliding expiration plus per-entry metadata (`CreatedAt`, `EagerRefreshAt`, `ETag`, `LastModifiedAt`, `Tags`). `CreatedAt` is the birth time the Family-2 read-time predicate compares against tag/clear markers.
- `CacheStoreEntryWrite<T>` - write descriptor carrying the value, expirations, eager stamp, validators, `CreatedAt` (stamped on every fresh write so a prior tag/clear marker cannot invalidate it), `Tags`, and `IsRestamp` (marks metadata-only restamps — `NotModified` extensions, fail-safe throttle restamps, the eager-refresh gate write — so multi-tier stores can skip cross-instance invalidation for them).
- `CacheTagInvalidation` - the single shared read-time predicate `IsInvalidated(createdAt, newestMarker)` so every tier agrees on when an entry is logically tag/clear-invalidated.
- `CacheEntryStamps` - single home of the fresh-write stamp math (`CreatedAt` birth time, fail-safe extends physical retention, eager threshold stamps the eager point, sliding clamps the logical lifetime) and of options/tags validation, so the coordinator and the providers' direct `UpsertEntryAsync` writes always agree.
- `FactoryCacheStoreExtensions.UpsertEntryAsync` - shared direct options-based upsert composed on the store primitive (read-before-write; stamps a fresh `CreatedAt`).
- `ICacheFactoryLockProvider` - optional cross-node coordination seam consumed when `CacheEntryOptions.UseDistributedFactoryLock` is set (adapter: `Headless.Caching.DistributedLocks`).
- `CacheStoreEntryExtensions` - shared `IsFresh`/`IsPhysicallyPresent` predicates so every provider and the coordinator agree on the expiration boundary (an entry is expired at the exact tick, `expiresAt <= now`).
- `FactoryCacheCoordinator.IsCallerCancellation` - shared predicate provider composites use so caller cancellation propagates while an unrelated/downstream `OperationCanceledException` activates fail-safe consistently.
- `SetupCachingCore.AddHeadlessCaching` - the single registration entry point: provider packages contribute deferred extensions through `Use*`/`Add*Tier`/`AddNamed` on the setup builder, and contributions are applied only after the setup gates pass.
- `HeadlessCachingSetupBuilder` / `HeadlessCacheInstanceBuilder` / `ICacheProviderOptionsExtension` - the builder surface provider packages extend: a default slot (exactly one `Use*`), role-keyed tier slots (at most one per reserved role), named instances (unlimited, unique non-reserved names, exactly one provider each), and cross-cutting extensions.
- `ICacheProvider` over the container's keyed `ICache` registrations; `AddHeadlessCaching` registers it automatically. `RegisteredNames` enumerates the `AddNamed` instances (default and tier role keys excluded) for validating a name before resolving.
- `CacheEventsHub` / `CacheEventsConfig` - the concrete dispatcher behind `ICache.Events` and its execution config. `CacheEventsHub` implements `ICacheEvents`, builds args only when the specific event has a subscriber (no allocation when unobserved), and runs each guarded handler — caught and logged, never propagated to the cache caller — on a background task by default. `CacheEventsConfig` (`SyncHandlers`, `HandlerErrorLogLevel`) carries the execution options, populated from the setup builder's `SyncHandlers` / `EventHandlerErrorLogLevel` (default `Warning`).
- Fail-safe, factory timeout, eager refresh, and background completion logs.

## Design Notes

Providers construct the coordinator directly with their `TimeProvider`, logger, and optional `ICacheFactoryLockProvider`; the Core package ships the `AddHeadlessCaching` entry point and the setup builder, not a provider. Provider packages queue deferred `ICacheProviderOptionsExtension` contributions that `AddHeadlessCaching` applies tiers → default → named → cross-cutting only after the per-slot gates pass — exactly one default provider, at most one tier per reserved role, no tier role already claimed by the default provider, unique non-reserved instance names with exactly one provider each, and no repeated `AddHeadlessCaching` call — so a failed setup leaves the service collection unchanged. Store read failures are treated as misses, fail-safe restamp writes are best-effort, and sliding re-arm writes are best-effort so a cached value can still be returned when the backing store is unhealthy. A provider composite can mark a physically-present stale `CacheStoreEntry<T>` with `ServeStaleImmediately` when a lower tier degraded during the read; the coordinator then returns that stale value without running the factory, but only when fail-safe is enabled. Cancellation is classified by token identity: the caller's own cancellation propagates and never activates fail-safe, while an `OperationCanceledException` from an unrelated or downstream token is treated as a failure that activates fail-safe. Sliding expiration is rejected together with fail-safe (one needs value reads to extend the logical deadline while the other needs logical expiration to expose a stale reserve) and together with eager refresh (both re-arm the logical lifetime).

Factory timeout selection is centralized in the coordinator. If fail-safe is enabled, a stale reserve exists, and `FactorySoftTimeout` is finite, the soft timeout governs. Otherwise a finite `FactoryHardTimeout` governs. Otherwise factory execution is unbounded except for caller cancellation. A finite soft timeout also bounds acquisition of the same per-key lock when stale data exists, so waiters and supported same-key re-entrant calls return stale instead of blocking behind an in-flight refresh. When no stale reserve exists, `LockTimeout` (default `Timeout.InfiniteTimeSpan`) bounds that acquisition instead, and a finite value makes the waiter degrade to a miss rather than block.

Eager refresh triggers off the entry's own `EagerRefreshAt` stamp, so any reader of an eager-stamped entry can refresh it with its current factory and options. The first reader past the eager point wins a zero-timeout per-key `TryLock`; everyone else returns the still-fresh value untouched. The winner double-checks the entry under the lock, then performs a gate write that clears the eager stamp before the factory starts, so other readers (including other nodes reading through a shared store) stop triggering while the refresh is in flight. The gate write is best-effort: when it fails, the refresh is skipped and the entry stays fresh and re-triggerable. An eager factory failure is logged and the entry is left untouched — it is still fresh, so there is no fail-safe restamp; natural expiry and fail-safe (when enabled) take over from there.

Conditional refresh and adaptive options run through one write path shared by the foreground factory, the soft-timeout background completion, and the eager refresh. A `NotModified` result re-stamps the existing last-known-good value as fresh with the context's current options, preserving its validators; the factory's adaptive `Options` replacement is re-validated before the write and an invalid mutation throws after the factory ran with nothing written. The throttle restamp applied when fail-safe activates preserves the stale entry's metadata (`ETag`, `LastModifiedAt`, `Tags`) but drops `EagerRefreshAt`, so a restamped stale reserve cannot trigger an eager refresh on top of the throttle.

When `CacheEntryOptions.UseDistributedFactoryLock` is set and a provider is registered, the coordinator layers a cross-node lock over the local per-key lock: acquire local, acquire distributed with the same wait budget, re-check the shared store (the loser of the cross-node race serves the winner's value), then run the factory. The lease transfers into soft-timeout background completions and eager refreshes so the cross-node guard stays held until the detached write lands, and release is best-effort with the lease TTL as the backstop. A throwing acquire (lock backend down, as opposed to `null` = held elsewhere) degrades through fail-safe: with `IsFailSafeEnabled` and a usable stale reserve the coordinator serves the stale value, restamps the throttle so per-call retries stop hammering the down backend, and logs a warning; without a usable reserve the provider's exception propagates, and caller cancellation always propagates as cancellation. Enabling the option without a registered `ICacheFactoryLockProvider` throws `InvalidOperationException` naming the adapter package.

The coordinator deliberately diverges from FusionCache on background cancellation. A soft-timed-out factory uses a detached internal token and can outlive the caller request. Hard timeouts cancel or abandon the factory and never allow background completion. The per-key no-duplicate-factory guarantee holds cleanly for cooperative factories; after the background ceiling abandons a token-ignoring factory, another factory may run for that key while the abandoned task continues untracked, but late timeout-path writes are gated off.

## Installation

```bash
dotnet add package Headless.Caching.Core
```

## Quick Start

`AddHeadlessCaching` is the registration entry point this package owns; the `Use*`/`Add*Tier` extensions come from the provider packages:

```csharp
var redis = ConnectionMultiplexer.Connect("localhost:6379");

services.AddHeadlessCaching(setup =>
{
    setup.UseRedis(options => options.ConnectionMultiplexer = redis); // default slot: exactly one Use* required
    setup.AddNamed(
        "sessions",
        i =>
            i.UseRedis(options =>
            {
                options.ConnectionMultiplexer = redis;
                options.KeyPrefix = "sessions:";
            })
    );
    setup.UseDistributedFactoryLock(); // cross-cutting opt-in (Headless.Caching.DistributedLocks)
});
```

Beyond the entry point, consumers do not use this package directly. Provider packages reference it to implement `GetOrAddAsync` and the options-based `UpsertEntryAsync`.

## Configuration

None.

## Observability

Emits OpenTelemetry metrics and traces under a single instrumentation name, `Headless.Caching` (both `Meter` and `ActivitySource`), exposed as `CachingDiagnostics.SourceName`. Register with `TracerProviderBuilder.AddCachingInstrumentation()` / `MeterProviderBuilder.AddCachingInstrumentation()` (typed helpers in the `OpenTelemetry.Trace` / `OpenTelemetry.Metrics` namespaces, `OpenTelemetry.Api` only — no SDK dependency), or subscribe by name. When no listener is attached the emit sites short-circuit via `CachingDiagnostics.IsEnabled`, so an unobserved cache pays no per-operation cost.

The `FactoryCacheCoordinator` owns the `cache.get_or_add` span plus the `headless.cache.requests` / `factory.executions` / `factory.duration` / `failsafe.activations` / `refreshes` instruments; providers add thin `writes` / `evictions` counters and the hybrid tier attribution. Every instrument carries `headless.cache.name` (the registered instance name, or `default`). The raw cache key is never a metric dimension and appears on spans only when the caching setup builder's `IncludeKeyInTraces` opt-in (default off) is enabled. See [docs/llms/caching.md](../../docs/llms/caching.md) for the full instrument table, span shape, and counting model.

Beyond metrics and traces, `cache.Events` (`ICacheEvents`) is a second, in-process consumer channel over the same signals — native .NET events (`Hit`/`Miss`/`Set`/`Remove`, factory/fail-safe/refresh, in-memory `Eviction`, hybrid `Invalidation`) dispatched by `CacheEventsHub`. Handlers run guarded on a background task by default; set `AddHeadlessCaching(s => s.SyncHandlers = true)` to run them synchronously on the firing thread and `s.EventHandlerErrorLogLevel` (default `Warning`) to set the handler-error log level. An event with no subscriber builds no args and does no work. `async void` handlers are unsupported (their exceptions bypass the guard) — keep handlers synchronous. See [docs/llms/caching.md](../../docs/llms/caching.md) for the full event list and execution semantics.

## Dependencies

- `Headless.Caching.Abstractions`
- `Headless.Extensions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`
- `OpenTelemetry.Api` (typed registration helpers only)

## Side Effects

- `AddHeadlessCaching` validates the setup gates, then applies provider contributions in tier → default → named → cross-cutting order; a failed setup registers nothing.
- Registers `ICacheProvider` as singleton (`TryAdd`) and a registration sentinel that makes a second `AddHeadlessCaching` call throw.
- Providers own coordinator construction; no other registrations.
