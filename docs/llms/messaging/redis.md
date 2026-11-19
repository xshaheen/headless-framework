---
domain: Messaging
packages: Messaging.Redis
---

# Redis Streams transport

> Part of [Messaging](../messaging.md). The `Headless.Messaging.Redis` transport: Redis Streams for both lanes, consumer groups, pending-entry claim, age-bounded trimming, and publish wake-ups.

## Headless.Messaging.Redis

Use this transport when Redis is already in the stack and you accept no broker-native dead-letter queue. Both lanes use retained Streams with Redis consumer groups, which the provider creates on subscribe.

### Setup

```bash
dotnet add package Headless.Messaging.Redis
```

```csharp
using StackExchange.Redis;

// Shares the IConnectionMultiplexer the app registers for its other Redis packages.
services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
setup.UseRedis();

// Or opens a connection of its own.
setup.UseRedis(options => options.Configuration = ConfigurationOptions.Parse("localhost:6379"));
```

**Verify:** the `messaging-broker` readiness check reports `Healthy` once a consumer reads its streams ([Broker reachability](operations.md#broker-reachability)), and the `describe` host manifest's `messaging.transports` shows `"redis"` for both lanes.

### Names and topology

- Stream keys are `headless:messaging:bus:{logical-name}` and `headless:messaging:queue:{logical-name}`.
- **Bus:** the consumer group on each Bus stream is named after the consumer identity, so each identity gets one copy and its replicas compete inside the group.
- **Queue:** the consumer group on the Queue stream is named after the message, so each message has one copy and its replicas compete.
- **Wire format:** a stream entry has two fields, `headers` (a JSON object) and `body` (the raw bytes).

### Connection sharing

With neither `Configuration` nor `ConnectionMultiplexer` set, `UseRedis` shares the `IConnectionMultiplexer` registered in the service collection, the one the Redis cache, lock, coordination, and blob providers use. With none registered it falls back to `localhost` on the default port.

- A shared multiplexer is never disposed by the transport, and `ConnectionPoolSize` does not apply to it.
- Sharing costs no throughput: the transport sends no blocking command, so its reads and acknowledgements pipeline with other commands, and its wake-up and reply channels use the multiplexer's one subscription connection.
- A connection string, `Configuration`, or `UseRedis(IConfiguration)` with a configured connection opens `ConnectionPoolSize` multiplexers of the transport's own (one by default), used in turn.
- Set only one of `Configuration` and `ConnectionMultiplexer`. Setting both fails validation.

### Configuration

Configure Redis connection and stream behavior through `RedisMessagingOptions`:

| Option | Default | Effect |
| --- | --- | --- |
| `StreamEntriesCount` | `100` | Entries one read takes from each stream. A full batch is followed at once by the next read, so a backlog drains without waiting the poll interval |
| `PendingClaimMinIdleTime` | 60 s | How long an entry stays pending before another consumer of the group claims it with `XAUTOCLAIM`, so a crashed consumer's entries move on within a minute. Must be positive |
| `IdleConsumerDeleteAfter` | 1 hour | How long a consumer with no pending entries stays idle before it is deleted from its group. `TimeSpan.Zero` keeps every consumer |
| `StreamMaxAge` | 7 days | Each publish trims entries older than this from a stream that has a consumer group (`XADD MINID ~`). `TimeSpan.Zero` keeps entries without an age limit. Otherwise it must exceed `PendingClaimMinIdleTime` |
| `EveryInstanceStreamMaxAge` | 5 minutes | Each publish trims entries older than this from a stream with no consumer group, which only every-instance readers read (cache invalidations, lock-release and settings-change signals). Applies even when `StreamMaxAge` is `TimeSpan.Zero`. At least 2 minutes |
| `WakeConsumersOnPublish` | `true` | Each publish follows its `XADD` with a `PUBLISH` on `{stream}:wake`, and subscribed consumers read the stream at once instead of at their next poll. `false` stops this process from publishing and subscribing to wake-ups |
| `ConnectionMultiplexer` | unset | A multiplexer to share instead of opening one |
| `ConnectionPoolSize` | `1` | Multiplexers opened from `Configuration`. Ignored for a shared multiplexer. Raise it only when large bodies hold up smaller commands on one socket |

**Routing affinity.** No native mapping. `RequireRoutingAffinity()` fails startup. A supplied `RoutingAffinityKey` travels as the envelope header, which a consumer tuned `Sequential()` orders by, and does not choose the stream.

### Trade-offs and limits

- **Pending window.** A durable consumer acknowledges an entry once the core admits it into the inbox, before its handler runs, so `PendingClaimMinIdleTime` bounds admission, not handler duration. A runtime subscription acknowledges after an inline handler returns, so set the option above that handler's longest run. Keep the time to admit one batch of `StreamEntriesCount` entries well below it, or another consumer claims entries still waiting, and the inbox discards the duplicates.
- **Rejects stay in place.** A rejected delivery stays pending at its position and is delivered again by the claim. The stream does not grow.
- **Retention is not acknowledgement-aware.** Trimming removes an entry whether or not a group read it. A group offline longer than `StreamMaxAge` misses the trimmed entries, and an entry that keeps failing admission is dropped once older than `StreamMaxAge`. Age is measured on the publishing process's clock. Approximate trimming can let entries outlive the limit slightly but never removes them early. No length cap (`MAXLEN`) is applied.
- **Streams without a consumer group keep entries for minutes.** A consumer group starts at entries added after its creation, and an every-instance reader starts at the tail it found on subscribe. A publisher learns whether a stream has a group with one `XINFO GROUPS` per stream: once for a stream that has one, and again every half `EveryInstanceStreamMaxAge` for a stream that has none. A server that refuses `XINFO` (an ACL without it) keeps `StreamMaxAge` and logs a warning per stream. Keep clock skew between publishers and the Redis server below half `EveryInstanceStreamMaxAge`.
- **Every-instance reads.** A group-less every-instance reader resumes from the last id it read after a failed read. When it could not read for half `EveryInstanceStreamMaxAge` or longer, the trim may have removed unread entries, so it raises the reconnect signal (`IOnSubscriptionEstablished` with `IsReconnect = true`) before it dispatches. On a stream that also has a consumer group, the reader skips entries older than `StreamMaxAge`.
- **Reads poll, woken by publishes.** StackExchange.Redis never sends a blocking `XREADGROUP BLOCK` over its shared multiplexer, so each consumer polls at `MessagingOptions.ConsumerPollTimeout` (default 1 s; each poll is an `XREADGROUP` plus a group check per stream). A wake-up ends the wait early. Under steady traffic a loop reads at most once every 50 milliseconds, so a busy stream adds at most 20 reads a second per loop. Each publish costs one `PUBLISH`, which a Redis Cluster forwards to every node. Pub/sub delivers at most once, so a lost wake-up only delays an entry to the next poll. Wake-ups need `PUBLISH` and `SUBSCRIBE` on the `{stream}:wake` channels. Where an ACL denies them, set `WakeConsumersOnPublish` to `false`, or consumers log one warning and keep polling. Set the same value on every process.
- **No broker delivery limit.** A `CustomHeadersBuilder` that keeps throwing leaves the entry pending and redelivered until `StreamMaxAge` trims it.
- **Delivery deadline.** Redis Streams have no per-message TTL. A late message is dropped by the consumer's `DeliverWithin` check.

### Design and runtime behavior

- **Consumer groups** are checked once per process and stream, not on every poll. A stream or group deleted while the host runs is re-created when the server answers `NOGROUP`. A connection or timeout failure while checking a group fails the read and counts toward [broker reachability](operations.md#broker-reachability). An error the server answered skips only that stream. When the server refuses the group of every subscribed stream (an ACL user without `XGROUP` rights, `LOADING`, `OOM`), the read fails and counts toward reachability.
- **Consumer names.** A process reads each group as `{group}:{machine name}:{slot}`. A process restarted on the same machine, such as a StatefulSet pod, reads under its old names, and its startup pass delivers the entries it left pending at once. A process whose machine name changes, such as a Deployment pod, leaves its pending entries to the claim after `PendingClaimMinIdleTime`. Two processes that share a machine name redeliver each other's pending entries at startup, and the inbox discards the duplicates.
- **Idle-consumer sweep.** Every `PendingClaimMinIdleTime`, each client runs one Lua script per stream that deletes consumers idle longer than `IdleConsumerDeleteAfter` with no pending entries. The sweep needs `EVAL`. Where an ACL denies it, set `IdleConsumerDeleteAfter` to `TimeSpan.Zero`.
- **Read loops.** A failed Redis call is retried with a back-off and counts toward [broker reachability](operations.md#broker-reachability); it never stops the subscription. A group subscription runs two reads: one claims stale pending entries and one reads new entries. When either read ends for any reason other than shutdown, the other stops too, and the consumer client stops listening with the read's exception. The subscription then reports unhealthy, the host logs EventId 37 with that exception (EventId 4146 when the read ended without one), and the transport check rebuilds it. An every-instance read that ends behaves the same way.
- **Shutdown** stops the read loops, then waits within the shutdown budget for them and for in-flight handlers to settle. An entry read after shutdown began, and an entry a handler never settles, stay pending for the claim.
- **Every-instance consumers** use a group-less polled read from the stream tail; see [Every-instance Bus delivery](consumers.md#every-instance-bus-delivery).
- **Request/reply** uses pub/sub on the literal channel `headless.reply.{32 hex}`, writes no key, and needs the same StackExchange.Redis `ChannelPrefix` on every host that exchanges requests; see [Provider support](request-reply.md#provider-support).
- **Ordering.** Ordered within a stream. An entry reclaimed after a failed delivery arrives beside newer ones. See [Transport-Specific Ordering](ordering.md#transport-specific-ordering).
- **Raw producers.** A raw entry's message id is `redis:{streamKey}:{entryId}`, and the `headers` field is optional; see [Foreign producers and raw payloads](foreign-producers.md#foreign-producers-and-raw-payloads).
