# Messaging Consumer-Model Evolution — Cluster 0 PR Split Plan

**Status:** Draft, awaiting approval.
**Date:** 2026-05-26.
**Source spec:** `docs/brainstorms/2026-05-25-messaging-consumer-model-evolution-requirements.md`
**Roadmap meta:** #217.

## Purpose

Break Cluster 0 (consumer-model evolution) into bounded, shippable PRs with explicit dependencies. The current `#217` lists Cluster 0 as a flat table of 10 bundles — that's a planning convenience, not a PR shape. This plan defines the actual implementation slicing.

## Constraints

- Cluster 0 blocks all other messaging clusters (1, 2, 14). Cannot be parallelized with them.
- Sub-PRs within Cluster 0 can be parallelized where dependencies allow.
- Greenfield project — breaking changes are acceptable. No type-forwarding shims, no deprecation aliases.
- Each sub-PR should be reviewable in a single session.

## PR Split

```
Tier 1 (parallel — mechanical, no semantic change):
   PR-0.1: Type relocation                          PR-0.2: Topic → MessageName rename
        \                                           /
         \_________________________________________/
                            ↓
Tier 2 (sequential — biggest design surface):
   PR-0.3: ForMessage<T> registration API
                            ↓
   PR-0.4: Layer 2 universal knobs + provider escape hatches
                            ↓
Tier 3 (parallel — independent file sets):
   PR-0.5: Dual-lane physical topology              PR-0.6: Dispatch-side intent absorption
   per provider + Kafka guard                       (selector + circuit-breaker keys)
```

---

## PR-0.1 — Type relocation

**Closes on merge:** #345

**Depends on:** none.

**Scope:** Complete the Bus/Queue.Abstractions split started by PR #340. Move shared types out of `Headless.Messaging.Abstractions/` into appropriate intent packages or a slimmed shared base. Spec §3 + §11.

**Types to relocate:**

| Type | From | To |
|---|---|---|
| `PublishOptions` | `Headless.Messaging.Abstractions` | `Headless.Messaging.Bus.Abstractions` |
| `MessageHeader` | `Headless.Messaging.Abstractions` | stays (shared base) |
| `BrokerAddress` | `Headless.Messaging.Abstractions` | stays (shared base) |
| `MessagePublishOptionsBase` | `Headless.Messaging.Abstractions` | stays (shared base) |
| `TransportMessage` | already in `Headless.Messaging.Abstractions` | no-op |
| `OperateResult` | already in `Headless.Messaging.Abstractions` | no-op |
| `EnqueueOptions` | already in `Headless.Messaging.Queue.Abstractions` | no-op |

The only real move in this PR is `PublishOptions` → `Bus.Abstractions`. (Earlier drafts also listed `TransportMessage` and `OperateResult` as moves; verified those types already live in `Headless.Messaging.Abstractions/`, likely from PR #340.)

**Files touched:**
- `src/Headless.Messaging.Abstractions/PublishOptions.cs` — moved out
- `src/Headless.Messaging.Bus.Abstractions/PublishOptions.cs` — new home
- All transport providers (8): `using` directive updates only for `PublishOptions`

**Acceptance criteria:**
- `Bus.Abstractions` and `Queue.Abstractions` carry their intent-specific types.
- Shared base types (`TransportMessage`, `OperateResult`, `MessageHeader`, `BrokerAddress`, `MessagePublishOptionsBase`) live in `Headless.Messaging.Abstractions`.
- No type-forwarding attributes.
- All provider transports build green with `using` directive updates only.
- `make build` clean; `make test` green.

**Risk:** mechanical refactor. Risk is missing a reference.

---

## PR-0.2 — Topic → MessageName rename

**Closes on merge:** none directly (paves the way for #341).

**Depends on:** none. Can land in parallel with PR-0.1.

**Scope:** Cross-cutting rename of `Topic` terminology that's broker-leaky. Spec §4 + §10.

**Renames:**

| Symbol | Before | After |
|---|---|---|
| `ConsumeContext.Topic` property | `Topic` | `MessageName` |
| `PublishOptions.Topic` property | `Topic` | `MessageName` |
| `EnqueueOptions.Topic` property | `Topic` | `MessageName` |
| `MessagingConventions.GetTopicForType<T>()` method | `GetTopicForType` | `GetMessageNameForType` |
| XML doc references | "topic" prose | "message name" prose |
| `MessagingConventionsExtensions` | helper names | mirror |

**Files touched:**
- `src/Headless.Messaging.Abstractions/ConsumeContext.cs`
- `src/Headless.Messaging.Abstractions/PublishOptions.cs` (post-relocation, may be in `Bus.Abstractions`)
- `src/Headless.Messaging.Queue.Abstractions/EnqueueOptions.cs`
- `src/Headless.Messaging.Abstractions/MessagingConventions.cs`
- `src/Headless.Messaging.Abstractions/MessagingConventionsExtensions.cs`
- All call sites across `Headless.Messaging.*` (~30–50 files estimate)
- All tests

**Acceptance criteria:**
- Zero remaining `Topic` references in messaging abstractions (verified by grep).
- All `*.Tests.*` and integration tests pass with renames.
- Provider-native `Topic` terminology (Kafka `TopicName`, ASB `TopicName`, SNS `Topic`) is left intact inside provider escape hatches.
- README + `docs/llms/messaging.md` updated to use `MessageName`.

**Risk:** mechanical. Coordination with PR-0.1 if both rename and relocate the same file — landing order should be 0.1 then 0.2, or 0.2 then 0.1, but not interleaved on the same files.

**Coordination note:** if PR-0.1 lands first, PR-0.2 sees the relocated `PublishOptions` in `Bus.Abstractions`. If PR-0.2 lands first, PR-0.1 sees the renamed `MessageName` property. Either order works; pick one at implementation time.

---

## PR-0.3 — ForMessage<T> registration API

**Closes on merge:** #341, #335

**Depends on:** PR-0.1 (types in right place), PR-0.2 (names settled).

**Scope:** The headline change. New message-centric registration API; legacy registration deleted. Spec §3 + §10.

**API shape:**

```csharp
services.ForMessage<OrderPlaced>(x =>
{
    x.MessageName("orders.order-placed");          // Layer 2 (PR-0.4 actually wires the hint)

    x.OnBus<NotificationsConsumer>();              // intent-free handler, bus lane
    x.OnQueue<InventoryConsumer>(q =>              // intent-free handler, queue lane
    {
        q.Group("inventory-service");              // align with existing IConsumerBuilder<T>.Group(string)
    });
});
```

**Deliverables:**
- `IServiceCollection.ForMessage<T>(Action<IMessageBuilder<T>>)` extension entry point.
- `IMessageBuilder<T>` with `OnBus<C>()`, `OnQueue<C>()`, optional per-consumer lambdas.
- `IConsumerBuilder<T>` per-consumer config (`Group`, Concurrency, Retry, etc.) — preserves existing `IConsumerBuilder<TConsumer>.Group(string)` method name (no `GroupId` rename).
- `MessagingConsumerRegistry` — replaces the old `ConsumerExecutorDescriptor`+intent pair.
- Message-name inference (default = type's full name, spec §10) + collision detection at startup.
- **Delete** `AddBusConsumer<TConsumer, TMessage>(...)` and `AddQueueConsumer<TConsumer, TMessage>(...)` from `Headless.Messaging.Core/ServiceCollectionExtensions.cs`.
- **Delete** any `Intent(IntentType)` builder method on the old `IConsumerBuilder`.

**Files touched:**
- `src/Headless.Messaging.Core/ServiceCollectionExtensions.cs` — old methods removed
- `src/Headless.Messaging.Core/Setup.cs` — new registration plumbing
- New: `src/Headless.Messaging.Core/Registration/MessageBuilder.cs`, `ConsumerBuilder.cs`, `MessagingConsumerRegistry.cs`
- All sample apps, tests, demos referencing old API

**Acceptance criteria:**
- `services.ForMessage<T>(...)` compiles and registers correctly.
- Same handler can be registered for both `OnBus` and `OnQueue` lanes simultaneously.
- **Publisher-only processes supported**: `ForMessage<T>(x => x.MessageName(...).PartitionBy(...))` with NO `OnBus` / `OnQueue` calls is a valid registration — it declares message metadata for publishing without requiring consumer handlers in the same process. Integration test: API gateway process registers message metadata + publishes; downstream consumer process registers the same metadata + handlers; partition key / message name flow correctly end-to-end.
- Message-name inference works; explicit `MessageName()` overrides inference.
- Startup throws on `MessageName` collision **across different C# types** (two distinct `T` mapping to the same `MessageName` string). Multiple `ForMessage<T>` calls for the **same** `T` from different assemblies/modules MUST merge consumer lists rather than throw — this preserves modular plug-in architectures where independent modules register their own consumers for shared message types.
- Old `AddBusConsumer` / `AddQueueConsumer` produce compile error (deleted, not deprecated).
- Integration tests register two consumers (one Bus, one Queue) for the same message type; both fire correctly.

**Risk:** highest in Cluster 0. Touches the core registration surface. Likely requires updates to ~all test files that register consumers.

---

## PR-0.4 — Layer 2 universal knobs + provider escape hatches

**Closes on merge:** none directly.

**Depends on:** PR-0.3 (builder exists).

**Scope:** Add the three Layer 2 universal hint knobs and provider escape hatches on the message + per-consumer builders. Spec §4.

**Layer 2 knobs (universal, message-scope):**
- `x.MessageName(string)` — already wired in PR-0.3 stub.
- `x.PartitionBy(Func<T, string>)` — selector for Kafka partition / ASB PartitionKey / SQS message group / NATS subject sharding. Unsupported providers warn at startup (UnsupportedHintBehavior.Log v1, single behavior).
- `x.CorrelationFrom(Func<T, string>)` — selector for cross-message correlation header. Universal — every provider supports it via headers.

**Correlation precedence (must be explicit in implementation):**

When multiple correlation sources exist on the publish path, apply in this order (highest priority wins):

1. Explicit `PublishOptions.CorrelationId` on the publish call — highest priority.
2. `CorrelationFrom` payload selector configured via `ForMessage<T>`.
3. Ambient `ConsumeContext.CorrelationId` propagation (when publishing inside a consume handler).
4. Default: framework-generated correlation ID.

**W3C `traceparent` header is isolated** from the correlation ID logic. It propagates independently via the OpenTelemetry pipeline and MUST NOT be overwritten by `CorrelationFrom`. The two carry different semantics (business correlation vs distributed trace span lineage) and must remain orthogonal.

**Deferred (not in this PR):**
- `OrderingBy` — design unresolved (spec §13.13). Sibling spec to file later.
- `UnsupportedHintBehavior` configurability — v1 hardcodes log-at-startup-continue (spec §13.14). Sibling spec to file later.

**Provider escape hatches (per-provider, message + per-consumer scope):**
- `x.UseRabbitMQ(rmq => { rmq.ExchangeType(...); rmq.RoutingKeyFromMessage(m => ...); })`
- `x.UseKafka(k => { k.IsolationLevel(...); })`
- Other providers as needed (ASB, AWS, NATS, etc.) — minimal v1 surface; add knobs as drivers appear.

**Files touched:**
- `src/Headless.Messaging.Core/Registration/MessageBuilder.cs` — knob methods
- `src/Headless.Messaging.Core/Registration/ConsumerBuilder.cs` — per-consumer escape hatches
- New: `src/Headless.Messaging.RabbitMq/Registration/RabbitMqMessageBuilderExtensions.cs`
- New: `src/Headless.Messaging.Kafka/Registration/KafkaMessageBuilderExtensions.cs`
- (Per-provider extension classes — one per provider that exposes escape hatch knobs)

**Acceptance criteria:**
- `PartitionBy` selector reaches the transport publish path and is honored by Kafka/ASB/SQS/NATS.
- `PartitionBy` on unsupported providers (e.g., RabbitMQ topic exchange) emits a startup warning naming the provider + knob, then continues.
- `CorrelationFrom` reaches the `Headers` on the outgoing `TransportMessage` for all providers.
- `UseRabbitMQ` block compiles only when `Headless.Messaging.RabbitMq` is referenced; same for other providers.
- Escape hatch at per-consumer scope overrides the message-scope value.

**Risk:** medium. New builder surface; per-provider extension classes are new code paths.

---

## PR-0.5 — Dual-lane physical topology per provider + Kafka guard

**Closes on merge:** #344

**Depends on:** PR-0.4 (topology knobs + builder available).

**Scope:** Implement spec §7 per-provider mappings — physical separation between Bus and Queue intent lanes. Spec §4 + §7 + §8.

**Per-provider changes:**

| Provider | Bus lane | Queue lane |
|---|---|---|
| RabbitMQ | Topic/fanout exchange | Direct exchange + queue |
| Azure Service Bus | Topic + subscription | Queue |
| AWS | SNS topic | SQS queue |
| Kafka | **Startup-fail** if `OnBus` registered (spec §8) | Topic + consumer group |
| NATS | Subject (pub/sub) | JetStream consumer |
| Pulsar | Per-subscriber unique Exclusive/Failover subscription name (one fan-out copy per consumer group) | Shared subscription (competing-consumer semantics) |
| Redis | Pub/Sub channel **(non-durable — see warning below)** | Streams + consumer group |
| InMemory | Per-lane channel | Per-lane channel |

**Redis Bus durability warning (v1 trade-off):**

Redis Pub/Sub is fire-and-forget. Messages published to a channel while a subscriber is offline or restarting are **lost forever**. This is a fundamental gap vs RabbitMQ / ASB / SNS / Pulsar Bus lanes (all durable).

V1 behavior: document the non-durable trade-off in `Headless.Messaging.RedisPubSub` README + `ConsumeContext` XML docs. No startup-fail (unlike Kafka §8) because Redis Pub/Sub is intentionally chosen for transient broadcast use cases.

Sibling spec to file: **"Redis durable bus lane via per-group Streams subscriptions"** — Cluster 0 §13 follow-up. Out of scope for Cluster 0.5.

**Kafka guard (spec §8 exact error wording):**
- At startup, scan registrations for any `ForMessage<T>` that includes `OnBus<C>()` AND the bus transport is Kafka.
- Throw with the spec §8 wording (gap explanation + 3 concrete workarounds: use a Bus-capable transport like RabbitMQ/ASB/SNS-SQS for that message; route via Queue intent if fan-out isn't actually needed; provide a custom Kafka fan-out emulation when the sibling spec lands).

**Files touched:**
- `src/Headless.Messaging.RabbitMq/RabbitMqBusTransport.cs`, `RabbitMqQueueTransport.cs`
- `src/Headless.Messaging.AzureServiceBus/AzureServiceBusBusTransport.cs`, `AzureServiceBusQueueTransport.cs`
- `src/Headless.Messaging.Aws/AmazonSnsBusTransport.cs`, `AmazonSqsQueueTransport.cs`
- `src/Headless.Messaging.Kafka/KafkaQueueTransport.cs` — add `KafkaBusTransport` only as a stub that throws at startup-time check (no runtime path)
- `src/Headless.Messaging.Nats/NatsBusTransport.cs`, `NatsQueueTransport.cs`
- `src/Headless.Messaging.Pulsar/*`
- `src/Headless.Messaging.RedisPubSub/*`, `Headless.Messaging.RedisStreams/*`
- `src/Headless.Messaging.InMemory/*`
- `src/Headless.Messaging.Core/Setup.cs` — Kafka guard check

**Acceptance criteria:**
- Conformance test: same message published via Bus lane does NOT reach a Queue-lane consumer registered for the same `MessageName`, and vice versa. Run on all 8 providers' integration test suites.
- Kafka + `OnBus` registration → startup throws with spec §8 error wording.
- `ConsumeContext.IntentType` correctly reflects the inbound lane on every provider.

**Risk:** highest in per-provider effort. Each provider is roughly 0.5–1 day. Conformance test infrastructure may need to expand.

---

## PR-0.6 — Dispatch-side intent absorption

**Closes on merge:** #334, #338

**Depends on:** PR-0.3 (registration key shape settled). Parallel with PR-0.5.

**Scope:** Update dispatch-side keys to include intent lane. With per-lane physical separation in PR-0.5, intent is structurally present in the inbound envelope; the keys must reflect that.

**Changes:**

1. **`IConsumerServiceSelector._MatchUsingName`** (`src/Headless.Messaging.Core/Internal/IConsumerServiceSelector.cs:~181`):
   - Old key: `(topic, group)`
   - New key: `(MessageName, intent-lane, consumer-registration)`
   - The lookup table now keys per-(MessageName, intent-lane) — handler registered via `OnBus` and `OnQueue` for the same message resolve to different `ConsumerExecutorDescriptor` instances.

2. **`ConsumerCircuitBreakerRegistry`** (`src/Headless.Messaging.Core/CircuitBreaker/ConsumerCircuitBreakerRegistry.cs`):
   - Old key: flat `string groupName`.
   - New key: reuse existing `CircuitBreakerGroupKeys.For(IntentType, string)` helper (already implemented; formats as `"{intentType:D}:{groupName}"`). Migrate the registry's dictionary type from `Dictionary<string, ...>` keyed on group → keyed on the composite key produced by `CircuitBreakerGroupKeys.For(...)`.
   - A queue-side handler's circuit breaker no longer affects the same handler's bus-side dispatch.

3. **`ConsumerExecutorDescriptorComparer`**: confirm key alignment with the new selector.

**Files touched:**
- `src/Headless.Messaging.Core/Internal/IConsumerServiceSelector.cs`
- `src/Headless.Messaging.Core/CircuitBreaker/ConsumerCircuitBreakerRegistry.cs`
- `src/Headless.Messaging.Core/Internal/ConsumerExecutorDescriptorComparer.cs`
- Related tests in `tests/Headless.Messaging.Core.Tests.Unit/`

**Acceptance criteria:**
- Same handler registered for `OnBus` + `OnQueue` for the same message dispatches to the correct lane based on inbound envelope's `IntentType`.
- Forced-failure on the queue lane leaves the bus lane's circuit breaker `Closed` (test from #338).
- Two-handler same-`(MessageName, group)` test from #334 passes.

**Risk:** medium. The selector is a hot path; need to ensure the key change doesn't regress lookup performance (microbench before/after).

---

## GitHub Sync Steps

Once this plan is approved:

### Step 1: File 6 implementation-tracking issues

For each PR-0.X:

- Title: `feat(messaging): Cluster 0.X — <one-line summary> (<closes-list>)`
- Labels: `messaging`, `enhancement`
- Body: copy from this plan's PR-0.X section + add Depends-on line + acceptance criteria + spec section anchors

### Step 2: Update #217 Cluster 0 section

Replace the current 10-bundle flat table with:
- The 3-tier ASCII diagram from the top of this plan
- A table listing the 6 sub-PR issues with their `Closes` lists and dependencies

### Step 3: Add cross-references to existing design / fix issues

Under the existing Status banner in each of:
- #341, #344, #345 (design issues)
- #334, #338 (fix issues)
- #335 (obsolete)

Add a line: `**Tracked by:** Cluster 0.X implementation issue #NNN`

### Step 4: Do NOT auto-close on merge

Use **manual close** after PR merge. Auto-close via `Closes #NNN` in PR body is risky here because a Cluster 0.X PR may only partially address a multi-issue close-list if scope shifts during implementation.

---

## Open Questions

1. **Split size** — is 6 PRs right? Alternatives:
   - 4 PRs (collapse 0.1+0.2 into one rename+relocate PR; collapse 0.5+0.6 into one dispatch+topology PR). Less granular, but fewer review rounds.
   - 8 PRs (split per-provider work in 0.5 into per-provider PRs — RabbitMQ alone, ASB alone, etc.). More granular, but Cluster 0 stretches longer.
2. **Branching strategy** — single long-lived `feature/messaging-cluster-0` branch with sub-PRs targeting it, or each sub-PR targets `main` directly? Single-branch is safer for coordinating Tier 2 ordering; direct-to-main is simpler ops.
3. **Sibling specs filing timing** — file the 5–7 sibling specs (cross-language wire, Kafka bus emulation, OrderingBy, etc.) now or after Cluster 0 lands? My lean: file as order-of-need surfaces, not all at once.

## Approval

When approved, execute Steps 1–3. Step 4 is a guideline applied at PR merge time.

---

## Review Log

### 2026-05-26 — antigravity review

Findings applied (7/7):

1. **Pulsar topology inversion (critical)** — PR-0.5: corrected Bus = per-subscriber Exclusive/Failover subscription, Queue = Shared subscription. Original draft had it reversed.
2. **PR-0.1 redundancy** — Removed `TransportMessage` and `OperateResult` from the relocation list; both already live in `Headless.Messaging.Abstractions/`. The only real move is `PublishOptions` → `Bus.Abstractions`.
3. **Publisher-only process blindness** — PR-0.3: added explicit support + acceptance criterion for consumer-free `ForMessage<T>` registrations (e.g., publisher API gateways register message metadata without handlers).
4. **Correlation ID precedence** — PR-0.4: added explicit 4-level precedence order + isolation rule for W3C `traceparent`.
5. **Redis non-durable Bus lane** — PR-0.5: added Redis Bus durability warning section + sibling-spec follow-up.
6. **Naming alignment** — PR-0.3: corrected sample to use `q.Group(...)` matching existing `IConsumerBuilder<T>.Group(string)`. PR-0.6: registry key change reuses existing `CircuitBreakerGroupKeys.For(IntentType, string)` helper rather than introducing a new tuple key.
7. **Registration collision strategy** — PR-0.3: clarified — startup throws only when two distinct C# types map to the same `MessageName` string; multiple `ForMessage<T>` calls for the same `T` from different modules MUST merge consumer lists.

Coaching ([DEPTH]: separating egress metadata from ingress configuration) — addressed inline in the publisher-only process fix (PR-0.3 acceptance criteria explicitly call out the publisher/consumer process separation).
