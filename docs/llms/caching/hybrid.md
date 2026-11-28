---
domain: Caching
packages: Caching.Hybrid, Caching.Hybrid.Messaging
---

# Hybrid cache

> Part of [Caching](../caching.md). A process-local L1 in front of a shared L2, kept coherent across hosts by a backplane: setup, backplane choice, L2 resilience, and auto-recovery.

## Rules

- Register `setup.AddMemoryTier()`, `setup.AddRedisTier(...)`, and `setup.UseHybrid()` in the same `AddHeadlessCaching` call. The hybrid becomes the default `ICache`.
- Give the hybrid an L2. `UseHybrid()` with no `IRemoteCache` (no `AddRedisTier(...)` and no named remote tier) throws `InvalidOperationException` when the cache is resolved. `Headless.Caching.InMemory` registers no `IRemoteCache` and cannot be L2.
- Choose exactly one backplane: `setup.UseRedisBackplane()`, `setup.UseMessagingBackplane()`, or `setup.UseSingleInstanceBackplane()`, or register your own `IHybridCacheBackplane`. Without one, the host fails to start, because a write on one host would leave every other host serving its stale L1 copy until `DefaultLocalExpiration` runs out. The last `Use*Backplane` call wins. Registering `Headless.Messaging` alone wires no invalidation.
- Set `DefaultLocalExpiration` shorter than the L2 duration when L1 freshness matters. When `DefaultLocalExpiration` is set, L1 never holds an entry longer than it, whatever the entry's `Duration`.
- An exception from a non-replayable write against L2 does not mean nothing changed locally; see [L2 failures](#l2-failures).
- Move every host of one service to the same backplane in one rollout. A Redis-backplane host and a messaging-backplane host do not see each other's invalidations.

## Choose a backplane

| Backplane | Package | Use when | Delivery and gaps |
| --- | --- | --- | --- |
| `setup.UseRedisBackplane()` | `Headless.Caching.Redis` | The hosts share the Redis server that serves L2, or another registered `IConnectionMultiplexer`, and run no broker. | Redis pub/sub on `{KeyPrefix}headless.caching.hybrid.invalidation`. Flushes L1 on the first subscription and after every restored connection. See [Redis backplane](providers.md#redis-backplane). |
| `setup.UseMessagingBackplane()` | `Headless.Caching.Hybrid.Messaging` | The hosts already share a Headless messaging transport that supports every-instance delivery. | An every-instance Bus consumer. Flushes L1 every time the subscription is established. See [Headless.Caching.Hybrid.Messaging](#headlesscachinghybridmessaging). |
| `setup.UseSingleInstanceBackplane()` | `Headless.Caching.Hybrid` | Exactly one host runs the cache: a single-node service, a worker that owns its cache alone, a test host. | Sends nothing. With a second host, both serve stale L1 entries until `DefaultLocalExpiration` runs out. |

Both transports deliver at most once. Each flushes L1 after a gap instead of trying to replay what it missed; L2 refills L1 on the next reads.

---

## Headless.Caching.Hybrid

Two-tier cache: an in-memory L1, a remote L2, and cross-host invalidation through the backplane you choose.

### Setup

```bash
dotnet add package Headless.Caching.Hybrid
```

```csharp
var redis = ConnectionMultiplexer.Connect("localhost:6379");

services.AddSingleton<IConnectionMultiplexer>(redis);
services.AddHeadlessCaching(setup =>
{
    setup.AddMemoryTier();
    setup.AddRedisTier(options => options.ConnectionMultiplexer = redis);
    setup.UseHybrid(options => options.DefaultLocalExpiration = TimeSpan.FromMinutes(5));
    // Every host evicts the L1 entries another host's writes made stale, over Redis pub/sub.
    setup.UseRedisBackplane();
});
```

```csharp
public sealed record Product(string Id, string Name);

public interface IProductRepository
{
    ValueTask<Product?> GetByIdAsync(string id, CancellationToken cancellationToken);
}

public sealed class ProductService(ICache cache, IProductRepository repository)
{
    public async Task<Product?> GetProductAsync(string id, CancellationToken ct)
    {
        var cached = await cache.GetOrAddAsync(
            $"product:{id}",
            token => repository.GetByIdAsync(id, token),
            new CacheEntryOptions
            {
                Duration = TimeSpan.FromHours(1),
                IsFailSafeEnabled = true,
                FailSafeMaxDuration = TimeSpan.FromHours(6),
                FactorySoftTimeout = TimeSpan.FromMilliseconds(250),
                FactoryHardTimeout = TimeSpan.FromSeconds(3),
            },
            ct
        );

        return cached.HasValue ? cached.Value : null;
    }
}
```

**Verify:** start the host. With no backplane registered, the start fails with `MissingRequiredServiceException` naming all three `Use*Backplane` calls; resolving `ICache` outside a started host throws `InvalidOperationException` with the same text. With the Redis tier, the `cache-redis` readiness check reports L2.

**Named tiers.** Point the hybrid at named L1 and L2 instances instead of the role-keyed tiers when a tier needs its own identity, such as a second hybrid or a tier other named consumers share:

```csharp
services.AddHeadlessCaching(setup =>
{
    setup.AddNamed("hot-l1", i => i.UseInMemory(options => options.MaxItems = 5000));
    setup.AddNamed("hot-l2", i => i.UseRedis(options => options.ConnectionMultiplexer = redis));
    setup.UseHybrid(options =>
    {
        options.LocalCacheName = "hot-l1"; // must implement IInMemoryCache
        options.RemoteCacheName = "hot-l2"; // must implement IRemoteCache
    });
    setup.UseRedisBackplane();
});
```

A named hybrid binds named tiers the same way: `setup.AddNamed("hot", i => i.UseHybrid(options => { options.LocalCacheName = "hot-l1"; options.RemoteCacheName = "hot-l2"; }))`. One backplane serves the default hybrid and every named one. A named hybrid receives only the invalidations published for its own name.

### Configuration

| Option | Default | Description |
| --- | --- | --- |
| `KeyPrefix` | `""` | Prefix for all cache keys. |
| `DefaultEntryOptions` | `null` | Default `CacheEntryOptions` for the option-less `GetOrAddAsync` overloads; `null` makes them throw. |
| `DefaultLocalExpiration` | 5 minutes | L1 TTL cap. `null` makes L1 use the L2 expiration. |
| `InstanceId` | Generated | This instance's id; a receiver drops messages carrying its own id. |
| `LocalCacheName` | `null` | Named `ICache` to use as L1; it must implement `IInMemoryCache` or resolution throws. `null` uses the default `IInMemoryCache`. |
| `RemoteCacheName` | `null` | Named `ICache` to use as L2; it must implement `IRemoteCache` or resolution throws. `null` uses the default `IRemoteCache`. |
| `EnableAutoRecovery` | `false` | Queue failed single-key L2 writes and removes, failed tag, clear, and flush marker writes, and failed invalidation publishes, and replay them when L2 or the backplane recovers. See [Auto-recovery](#auto-recovery). |
| `AutoRecoveryMaxItems` | 128 | Most pending recovery items, one per key. On overflow the item expiring soonest goes, which may be the incoming one. |
| `AutoRecoveryMaxRetries` | 8 | Failed replays before an item is dropped with a warning. |
| `AutoRecoveryDelay` | 5 seconds | How often the recovery loop runs, and the back-off after a failed replay. |
| `AllowBackgroundDistributedCacheOperations` | `false` | Fire and forget the L2 write and the backplane publish of additive writes: the `GetOrAddAsync` factory write, `UpsertAsync`, and `UpsertAllAsync`. The caller returns after the L1 write, so L2 and peers briefly lag L1. A failed background write is queued for replay under `EnableAutoRecovery`, else logged. Removes, `Try*`, atomic and set operations, and reads stay synchronous, because their result is the L2 answer. |
| `DistributedCacheSoftTimeout` | `Timeout.InfiniteTimeSpan` | Longest wait for an L2 read that can fall back to an L1 stale reserve or a miss. |
| `DistributedCacheHardTimeout` | `Timeout.InfiniteTimeSpan` | Longest wait for an L2 read with no local reserve. Must exceed `DistributedCacheSoftTimeout` when both are finite. |
| `DistributedCacheCircuitBreakerDuration` | `TimeSpan.Zero` | How long to skip L2 after a failure other than cancellation. Zero turns the breaker off. |
| `ReThrowDistributedCacheExceptions` | `false` | Rethrow an L2 read or factory-write failure (other than cancellation) instead of degrading. Direct reads and the factory and `UpsertEntryAsync` writes surface it. Timeouts and an open circuit still degrade, a sliding re-arm stays best-effort, and a failed store read inside `GetOrAddAsync` still falls through to the factory. |
| `ReThrowBackplaneExceptions` | `false` | Rethrow a backplane publish failure (other than cancellation) after logging and any recovery queueing. It surfaces on synchronous writes; a background publish only logs it. |

At startup the hybrid logs warnings, never failures, for valid but questionable settings: fail-safe with no window beyond `Duration`, a finite `FactorySoftTimeout` without fail-safe, a very high `EagerRefreshThreshold`, or a very large `AutoRecoveryDelay`.

### Reads

- A read checks L1, then L2. A fresh L2 value fills L1, unless a local write, removal, or invalidation of that key landed after the L2 read began; then the fill is dropped, so an older value never lands in L1 after a peer's newer invalidation.
- A logically stale L2 entry does not fill L1 on a plain read. Fail-safe activation and background refresh still write through both tiers.
- A Redis sliding hit without a current idle deadline is served from L2. Hybrid caches it in L1 only when both expiry deadlines are known.
- An L1 miss on `GetSetAsync` or `GetAllSetMembersAsync` serves straight from L2 without filling L1, so a paged read never replaces a full local set. Sets enter L1 only through the hybrid's `SetAddAsync`.
- A sliding L1 hit is checked against L2 before the re-arm, so L2 keeps `Duration` as the absolute cap. With L2 unreachable, a fresh L1 sliding value still returns but is not re-armed.
- Fail-safe, factory timeouts, eager refresh, and conditional refresh behave as in [Factory-backed reads](factory.md); a stale reserve can come from L1 or L2, and every refresh writes both tiers.

### Writes and invalidation

- A write updates L2, then L1, then publishes an invalidation. A successful `TryInsertAsync` publishes too, because L2 can lose a key (eviction, a manual delete) while a peer still holds it in L1.
- Factory value writes publish like explicit upserts: cold-miss writes, soft-timeout background writes, eager refreshes, and conditional `Modified` writes. Metadata-only restamps (`NotModified`, the fail-safe throttle) publish nothing, because peers' cached bytes stay identical.
- A publish failure never fails an upsert. It is logged, and replayed under `EnableAutoRecovery`. Until then, peers keep their L1 copy until its TTL or the next successful invalidation.
- `ExpireAsync` expires both tiers and, when the key existed, publishes an `Expire` invalidation, so peers logically expire their L1 copy and keep its reserve instead of removing it.
- `RemoveByTagAsync` and `ClearAsync` bump the local L1 marker first, so local invalidation always lands even with L2 down. Then they publish, then bump the L2 marker under the circuit breaker; an L2 failure is logged and trips the breaker. Under `EnableAutoRecovery` a skipped or failed L2 marker bump is replayed with its original timestamp and published again. Without it, the L2 marker is not replayed: a host relying only on the shared marker (a late joiner, or one whose marker cache expired) sees the invalidation only when each entry's TTL ends. L1 and peers reached by the publish are not affected.
- `FlushAsync` publishes a flush: receivers wipe L1 and see a logical L2 remove marker stamped with the sender's time.
- **Atomic numeric operations** (`IncrementAsync`, `SetIfHigherAsync`, `SetIfLowerAsync`) drop the key from L1 instead of writing their result into it, and the next read fills L1 from L2. Concurrent operations can finish in either order, so writing each result could leave an older total in L1.
- **Per-tier skip writes.** A factory write guards against concurrent changes only on the tier its snapshot came from. A concurrent write that touched only the other tier is not detected. Only `SkipMemoryCacheWrite` and `SkipDistributedCacheWrite` writes can race this way.

### L2 failures

- `DistributedCacheSoftTimeout` bounds an L2 read that has a fallback. Inside `GetOrAddAsync`, a timed-out L2 read with an L1 stale reserve serves that reserve and skips the factory. A plain read degrades to a miss.
- `DistributedCacheHardTimeout` bounds an L2 read with no local reserve.
- `DistributedCacheCircuitBreakerDuration` skips L2 for a while after a failure other than cancellation. Reads degrade to L1 or a miss, and additive writes update L1 without waiting on L2.
- When a non-replayable write fails against L2 (removes, `Try*` compare-and-swap, atomic numeric operations, set mutations, prefix sweeps), the hybrid removes the affected L1 entries, or logically expires them for `ExpireAsync`, and then rethrows. This node stops serving values whose L2 state is unknown.

### Auto-recovery

`EnableAutoRecovery = true` is degraded-mode behavior, not a guarantee. A failing single-key L2 write succeeds against L1, logs a warning, and is replayed later, so L2 and other hosts lag L1 until the replay lands. Bulk, atomic, and set operations are never queued and still surface failures.

- **One pending item per key.** A newer set or remove replaces a queued item. A publish never replaces a queued set or remove, because a successful replay of the set or remove publishes the invalidation itself, stamped with the original write time. When that publish fails, a publish is queued in its place under the same retry limit.
- **L1 decides.** A queued set replays only while the L1 entry still carries the stamp that write produced; otherwise it is dropped as obsolete. Any successful L2 write of the key clears its pending item.
- **Invalidations win.** A replayed set keeps its original birth time and expiry, so a tag or clear issued on another node between the write and its replay still invalidates it. An invalidation from another host drops older queued items for that key, and a single-key invalidation older than a pending local item is ignored. Under an outage, concurrent writers converge on the last writer's value once every node has replayed. A message without a timestamp counts as newer. Tag invalidations do not drop queued items.
- **Custom L2.** An L2 that does not implement `IFactoryCacheStore` gets a plain upsert on replay, born at replay time, so only invalidations this node has already seen stop the replay.
- **Back-off.** Items with no expiry of their own (removes, publishes) are kept for `AutoRecoveryDelay × AutoRecoveryMaxRetries`. A replay pass runs oldest first and stops at the first failure, then waits `AutoRecoveryDelay`, so a long outage does not become a retry storm.
- Tag, clear, and flush marker writes replay at their original timestamp, raise-only, and publish again.

### Custom L2 and custom backplane

- **Custom L2.** An `IRemoteCache` that does not implement `IFactoryCacheStore` fills L1 without tags or birth time on a cold read. Such L1 entries ignore `RemoveByTagAsync` and serve until their physical TTL; key invalidation and `ClearAsync` still reach them. Implement `IFactoryCacheStore` and `ISeedableTagMarkerCache` on the custom L2; see [Custom cache providers](custom-provider.md).
- **Custom backplane.** Implement `IHybridCacheBackplane` and register it:
  - **Publish.** Send the `CacheInvalidationMessage` unchanged and complete only once the transport accepted it; throw otherwise. The cache logs the failure (`FailedToPublishCacheInvalidation`), queues a single-key invalidation for replay under `EnableAutoRecovery`, and rethrows under `ReThrowBackplaneExceptions`.
  - **Deliver.** Hand every delivered message to each registered `IHybridCacheInvalidationListener` through `OnInvalidationAsync`. Delivering the sender's own messages is fine: a receiver drops a message whose `InstanceId` is its own.
  - **Flush after a gap.** Call `OnSubscriptionEstablishedAsync` on each listener every time the subscription is established: the first time (L1 can warm before the subscription is live) and after every reconnect, once the new subscription is confirmed. The listener flushes the L1 of every hybrid; the flush publishes nothing.
  - **Resolve lazily.** Resolve the listeners on the first delivery, not in the backplane's constructor, because they depend on the hybrid caches, which depend on the backplane.

### Runtime behavior

- `setup.UseHybrid(...)` registers the hybrid as a singleton, as the default `ICache`, as `ICache<T>`, and under the role key `CacheConstants.HybridCacheProvider`. `setup.AddNamed(name, i => i.UseHybrid(...))` registers it as a keyed `ICache` under `name`.
- Registers one `IHybridCacheInvalidationListener` (`TryAddEnumerable`) that routes each message to the default hybrid or the named hybrid it names, and logs, never throws, a failure to apply one. It declares `IHybridCacheBackplane` as a service the host start requires, so the backplane can be chosen before or after `UseHybrid`, or registered outside `AddHeadlessCaching`.
- `setup.UseSingleInstanceBackplane()` replaces the backplane with one that sends nothing.
- With `EnableAutoRecovery`, a recovery timer driven by the registered `TimeProvider` runs.
- Implements `IBufferCache`: an L1 hit copies once into the caller's buffer, and an L1 miss reads L2 and fills L1. See [Byte payloads](adapters.md#byte-payloads).
- Events: the hybrid's own `cache.Events` carry the get-or-add result (`Tier = hybrid`) and `Invalidation`. Per-tier reads appear under `cache.Events.Memory` and `cache.Events.Distributed`. L1 evictions appear on the L1 cache's own `Events.Eviction`. See [Observability](observability.md#events).

---

## Headless.Caching.Hybrid.Messaging

Messaging backplane for `Headless.Caching.Hybrid`: sends L1 invalidations to every host over the Headless messaging bus.

### Setup

```bash
dotnet add package Headless.Caching.Hybrid.Messaging
```

```csharp
services.AddHeadlessMessaging(messaging =>
{
    messaging.UseRedis("localhost:6379");
    // Messaging needs one storage. The invalidation consumer is every-instance and keeps no inbox, so this host
    // needs no inbox guarantee; a host that also runs durable consumers on raw PostgreSQL storage opts down with
    // Options.MinimumInboxGuarantee = InboxGuarantee.Durable.
    messaging.UsePostgreSql(configuration.GetConnectionString("Messaging")!);
});
services.AddHeadlessCaching(setup =>
{
    setup.AddMemoryTier();
    setup.AddRedisTier(options => options.ConnectionMultiplexer = redis);
    setup.UseHybrid();
    setup.UseMessagingBackplane();
});
```

**Verify:** start the host. On a transport without every-instance delivery (AWS SNS/SQS), the start fails naming the consumer and the provider. Otherwise, a write on one host makes the next read of that key on another host come from L2, not its old L1 copy.

### Runtime behavior

- **Wire contract.** Invalidations travel as `CacheInvalidationMessage`, named `headless.caching.hybrid.invalidation` (`CacheInvalidationMessage.MessageName`), contract version `1`, consumed under the identity `headless.caching.hybrid.invalidation` (`HybridCacheInvalidationConsumer.Identity`). The host's naming conventions (`UseConventions`) do not rename it; only `MessagingOptions.MessageNamePrefix` applies, as to every message. A host on an older build that registered the consumer through `UseHybrid` interoperates with a host on `UseMessagingBackplane()`.
- **Every-instance delivery.** Each process opens its own subscription, named from the identity and the host's instance id, and the broker removes it when the process stops. See [every-instance Bus consumers](../messaging/consumers.md#every-instance-bus-delivery).
- **At most once.** A process receives only invalidations published while it is subscribed. Nothing is stored or retried: a failure to apply one is logged and the message is committed. Invalidations publish with `DeliveryMode.Direct` as system messages (`IsSystemMessage = true`), so an open unit of work neither captures nor delays them. On Redis Streams the invalidation stream keeps entries only for `RedisMessagingOptions.EveryInstanceStreamMaxAge` (5 minutes by default), not the 7-day `StreamMaxAge`.
- **L1 flush on every establishment.** Each time the subscription is established, the first time included, and again whenever it is re-established (`IsReconnect = true`: the transport recovered, or the host rebuilt its consumer clients after a broker failure or a topology change), every hybrid's L1 is flushed. A clean start flushes an empty L1, which costs nothing.
- **Transports.** InMemory, NATS, RabbitMQ, Redis, and Azure Service Bus support every-instance delivery. AWS SNS/SQS does not; use `UseRedisBackplane()` there.
- **`ConsumeOnly` hosts** still run the invalidation consumer, because `ConsumeOnly` never filters every-instance consumers.
- **Registration order.** Caching and messaging may be registered in either order; the contribution stays inert until messaging starts. Do not register the consumer yourself. Tune it by identity, for example `services.ConfigureMessaging(m => m.Tune(HybridCacheInvalidationConsumer.Identity, c => c.Concurrency(4)))`.
- Replaces the `IHybridCacheBackplane` registration (`services.Replace`); a later `Use*Backplane` call replaces it in turn.
- A publish awaits the bus, so a broker failure reaches the hybrid's auto-recovery and `ReThrowBackplaneExceptions`.
