---
title: Shared broker client pool with keyed lazy senders and fault-evicting retry
date: 2026-07-15
category: architecture-patterns
module: Headless.Messaging.AzureServiceBus
problem_type: architecture_pattern
component: service_class
severity: high
applies_when:
  - "A broker SDK client owns one multiplexed connection and per-destination senders/producers must be cached at the app level"
  - "Two or more DI singletons (bus + queue transports, publishers) would otherwise each create their own broker client"
  - "Shutdown must dispose per-destination resources before the shared connection, idempotently, while publishes may still be in flight"
tags: [connection-pool, lazy-initialization, concurrent-dictionary, dispose-ordering, azure-service-bus, messaging-transport, faulted-lazy-eviction, thread-safety]
---

# Shared broker client pool with keyed lazy senders and fault-evicting retry

## Context

The Azure Service Bus provider originally gave each transport (`AzureServiceBusTransport` for topics, `AzureServiceBusQueueTransport` for queues) its own private `ServiceBusClient` guarded by a non-atomic `_client ??=` inside per-destination `Lazy<ServiceBusSender>` factories. Three defects followed (issue #348, fixed in PR #688 — on branch `xshaheen/bus-pool`, unmerged as of this writing):

- Concurrent first publishes to *different* destinations raced `_client ??=` and could create multiple clients per namespace, all but one leaked. The per-key `Lazy` protected each sender, **not the shared client underneath** — fine-grained leaf locking with an unguarded shared root.
- Co-registering bus + queue always created two clients, i.e. two AMQP connections to one namespace.
- `DisposeAsync` disposed only the client; senders were never drained, with no ordering guarantee.

Prior art alignment: every other transport in this repo (Kafka, NATS, Pulsar, RabbitMQ, Redis) already injects a DI-singleton pool/factory into stateless transports; ASB had kept the inline-ownership shape inherited from the upstream CAP port.

## Guidance

Own the broker client and the per-destination sender cache in **one internal DI-singleton pool** shared by all publishers, modeled on `NatsConnectionPool`. Reference implementation: `src/Headless.Messaging.AzureServiceBus/IAzureServiceBusClientPool.cs`.

The pattern has two layers because the SDK pools only one of them:

- **Connection layer** — `ServiceBusClient` *is* the connection pool (one client = one multiplexed AMQP connection; all senders/receivers share it). The app's only job is to hold exactly one.
- **Sender layer** — `ServiceBusClient.CreateSender(name)` allocates a **new** sender (new AMQP link) on every call; the SDK never dedupes. The app must cache senders keyed by entity path.

Key mechanics:

```csharp
private readonly ConcurrentDictionary<string, Lazy<ServiceBusSender>> _senders = new(StringComparer.Ordinal);
private readonly Lock _clientLock = new();
private ServiceBusClient? _client;
private int _disposed;
```

1. **Senders: `GetOrAdd` + `Lazy(ExecutionAndPublication)` + eviction-on-fault.** `Lazy<T>` caches factory exceptions forever, which would permanently poison a destination after one transient failure. On throw, evict with the `KeyValuePair` overload — reference-equality on the value means a concurrent thread's *fresh* entry is never evicted by a stale loser:

   ```csharp
   try { return lazy.Value; }
   catch
   {
       _senders.TryRemove(new KeyValuePair<string, Lazy<ServiceBusSender>>(entityPath, lazy));
       throw;
   }
   ```

2. **Client: hand-rolled double-checked lock, deliberately NOT `Lazy<T>`.** The client factory must retry after failure; `Lazy` would cache the exception without the same eviction bookkeeping the sender path already needs. Volatile fast path, creation and the disposed re-check inside one `Lock`. Client construction performs no I/O (the AMQP connection is established lazily by the SDK), so holding the lock across the factory is cheap.

3. **Disposal: one-shot, capture-and-null under the creation lock, drain in `finally`-guarded order.**

   ```csharp
   if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

   ServiceBusClient? client;
   lock (_clientLock) { client = _client; _client = null; }   // (a)

   try
   {
       // drain materialized senders (IsValueCreated) in parallel   // (b)
       await Task.WhenAll(senderDisposals).ConfigureAwait(false);
   }
   finally
   {
       if (client is not null) await client.DisposeAsync().ConfigureAwait(false);  // (c)
   }
   ```

   - (a) closes two shutdown races at once: a publish that passed the entry disposed-check can no longer receive the disposed client from the lock-free fast path (it falls into the slow path, which re-checks `_disposed` under the lock and throws), and a client created concurrently with dispose is either visible here or its creator observed `_disposed` — no leaked connection either way.
   - (b) senders close independent AMQP links — drain them in parallel, before the client.
   - (c) the `finally` is load-bearing: disposal is one-shot, so a single faulted sender dispose without it would permanently skip client cleanup. This exact bug survived two review passes and was caught only when three independent reviewers (including a cross-model pass) converged on the same line.

4. **Transports stay stateless.** They inject the pool and implement `DisposeAsync() => ValueTask.CompletedTask` — matching the repo-wide rule that a client instance disposes only what it owns and never tears down shared pools (see the transport provider guide).

5. **Test seam.** An internal constructor accepts `Func<Options, ServiceBusClient>` so tests substitute a counting factory — without it, "exactly one client per namespace" is unassertable without a live broker. Test shapes worth copying from `tests/Headless.Messaging.AzureServiceBus.Tests.Unit/AzureServiceBusClientPoolTests.cs`: a `TaskCompletionSource`-gated 1,000-task stress asserting one creation; dispose-order recording via substitute callbacks; concurrent double-dispose; a factory blocked mid-creation racing `DisposeAsync` (asserts no leak); a sender blocked mid-creation racing `DisposeAsync` (asserts dispose does not hang).

## Why This Matters

- One client per namespace = one AMQP connection instead of N — the SDK multiplexes everything over it; duplicate clients are duplicate TCP/AMQP connections that also fragment SDK-level flow control.
- The failure modes this shape prevents are silent-in-CI, loud-in-production: leaked connections from first-use races, permanently poisoned destinations from `Lazy` exception caching, and unreclaimable clients from faulted drains only surface under concurrency or shutdown.
- A global `SemaphoreSlim(1,1)` around all creation (the upstream CAP shape) is simpler but serializes sender creation across unrelated destinations; the per-key `Lazy` + separately-guarded client keeps first-use concurrency without the shared-root race.

## When to Apply

- Adding or reworking any broker/provider whose SDK exposes a connection-owning client plus per-destination senders/producers/channels.
- Two-plus DI singletons need the same broker connection (publish transports, dispatchers) — hand them one pool, never per-consumer clients.
- Reviewing code where a shared field is guarded by `??=` inside per-key lazy factories — the per-key guard does not protect the shared root; that is this pattern's trigger bug.
- Receive-side sharing works through the same pool but with inverted disposal ownership: consumers obtain the shared client (`GetClient()`) to create processors, dispose only their processors, and never the client — the container-owned pool disposes it after the bootstrapper has stopped all consumers (issue #687).

## Examples

Before (racy, duplicated per transport):

```csharp
private ServiceBusClient? _client;
private ServiceBusSender _CreateSender(string topicPath)
{
    _client ??= new ServiceBusClient(...);   // non-atomic; races across destinations
    return _client.CreateSender(topicPath);
}
```

After (shared pool, both transports inject `IAzureServiceBusClientPool`):

```csharp
public ServiceBusSender GetSender(string entityPath)
{
    ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    var lazy = _senders.GetOrAdd(entityPath, static (path, pool) =>
        new Lazy<ServiceBusSender>(() => pool._CreateSender(path), LazyThreadSafetyMode.ExecutionAndPublication), this);
    try { return lazy.Value; }
    catch { _senders.TryRemove(new KeyValuePair<string, Lazy<ServiceBusSender>>(entityPath, lazy)); throw; }
}
```

## Related

- `docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md` — the PR-194 concurrency review gate this pattern conforms to (no volatile-bool gates, idempotent concurrent dispose, hold the lock through the dispose sequence); its concurrent double-dispose test shape is reused here.
- `docs/solutions/guides/messaging-transport-provider-guide.md` — ownership rule the transports follow: dispose only owned resources, never shared pools.
- `docs/solutions/architecture-patterns/named-instance-keyed-provider-registration.md` — precedent for container-owned disposable pools with no-op consumer disposal.
- Issue #348 (the defect), PR #688 (the fix), issue #687 (consumer-side client sharing follow-up).
