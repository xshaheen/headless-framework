---
date: 2026-05-25
topic: messaging-consumer-model-evolution
status: draft
issues: [341, 344, 345]
---

# Messaging: Consumer Model, Intent Lanes, and Abstractions Layout

## 1. Decision Summary

Resolve three gating design tickets that block downstream messaging clusters:

- **#341 — Consumer-intent coupling.** Adopt topology-declared intent with a message-centric registration API. No message-type markers. `IConsume<T>` remains the single handler interface. Replace `AddBusConsumer` / `AddQueueConsumer` with `services.ForMessage<T>(x => ...)`.
- **#344 — Cross-intent leakage.** Define two logical **intent lanes** (bus, queue). The framework declares per-lane topology from registrations; the **broker performs routing**. Cross-intent leakage is structurally prevented for framework-managed paths.
- **#345 — Abstractions layout.** Move `PublishOptions` from `Messaging.Abstractions` → `Bus.Abstractions` for symmetry with `EnqueueOptions` in `Queue.Abstractions`. Extract a shared `ITransport` base in `Messaging.Abstractions`. `MessagePublishOptionsBase` stays in `Messaging.Abstractions` as the shared base.

Greenfield project — no compat shims, no type-forwarding. Old APIs are deleted in the same PR cluster that lands the new ones.

### Positioning note: one opinion, by design

Headless Framework is positioned as **unopinionated, zero lock-in**. This spec takes **one deliberate opinion**: intent is a first-class concept and the framework guarantees per-lane separation (the bus and queue lanes never share broker topology for the same message type). Every other dimension — handler shape, transport choice, serializer, middleware, naming convention, topology refinement, broker primitive — stays unopinionated and is controlled by the consumer or the provider.

Why this one opinion is worth the deviation: silent cross-intent leakage at the broker level is a recurring real-world bug class (a misconfigured queue subscriber receives bus messages, or vice versa) that downstream packages cannot patch around. Adopters comparing to MassTransit / Wolverine — which let endpoints reshape topology freely — should expect Headless to feel slightly more constrained at the registration site in exchange for the structural guarantee. Section 12 lists the alternatives we considered and rejected.

### Target user

Primary user: **a senior .NET developer evaluating Headless Framework against MassTransit / Wolverine / NServiceBus / Rebus** for a new backend service. They are messaging-literate, comfortable with broker semantics, and reading the spec to decide whether Headless's smaller core and intent-lane model justifies the switch.

This adjudicates several tradeoffs in the spec:

- **Defaults bias toward correctness over ergonomics.** Failing startup on a Kafka+Bus registration is the right call for this user — they want loud failure at deploy time, not a quiet runtime surprise.
- **Error messages assume framework familiarity.** The Kafka Bus error wording (§8) names broker primitives (consumer group, topic) and explains the gap directly; it does not hand-hold.
- **Comparison sections (§4, §12, §13) are first-class spec content.** The primary user is actively benchmarking against alternatives; pretending the alternatives don't exist would be less useful than naming them honestly.

Junior .NET devs writing their first consumer are a secondary audience served by the no-config one-liner registration (`services.ForMessage<T>(x => x.OnBus<C>())`), but they are not the audience the spec's defaults are tuned for.

### Terminology

Three terms appear throughout this spec; they refer to distinct architectural layers:

| Term | Meaning |
|---|---|
| **Broker** | The messaging infrastructure itself — a RabbitMQ server, a Kafka cluster, an Azure Service Bus namespace, an AWS SNS/SQS region. Brokers route messages; the framework does not (§5). |
| **Transport** | A named binding to a broker, registered at host setup (e.g., `services.AddKafkaMessaging(...)` registers a transport under the well-known name `"kafka"`). Consumers pin to a transport via `q.Transport("kafka")` (§3 "Transport name registry"). A host can register multiple transports — even multiple instances of the same provider with different broker connections. |
| **Provider** | The .NET package that implements a transport (`Headless.Messaging.Kafka`, `Headless.Messaging.RabbitMQ`, etc.) and declares its supported intent lanes (§6). |

---

## 2. Non-Goals

- **Cross-language wire contract.** A wire-format spec (required headers, schema name/version, JSON envelope, manifest export, Python/Node/Go interop, schema registry) is **deferred to a sibling brainstorm**. This spec only asserts a boundary: the design must not require CLR type names, .NET assembly names, C# marker interfaces, or framework-specific payload formats on the wire. A detailed cross-language wire contract is out of scope here.

  > **Wire-coupling risk.** Two Layer 2 knobs are wire-shaping and may be adjusted when the wire spec lands: `MessageName` (becomes the wire-level message identity header) and `CorrelationFrom` (populates the wire correlation ID). The registration-API shape (`x.MessageName(...)`, `x.CorrelationFrom(...)`) is stable; what may shift is the exact header name, value format, or required-vs-optional status on the wire. If you adopt v1 before the wire spec lands, expect the broker payloads / headers around these two knobs to change. Treat the API surfaces as committed; treat the wire encoding as pending.
- **Saga / process-manager support.** Not gated by these decisions. Separate brainstorm.
- **Request / response (RPC-style messaging).** A consumer that sends and awaits a correlated reply on a temporary queue. MassTransit, Wolverine, and Rebus all support this. Out of scope for v1; tracked in §13. Possible v2 shapes: a third intent lane (`Rpc`), or a wrapper over Queue intent with framework-managed correlation and a reply-channel.
- **Scheduled / delayed delivery.** Publishing a message with a future visibility / delivery time. Native primitives differ per provider (RabbitMQ delayed-message plugin, Kafka KIP-389, ASB scheduled enqueue, SQS visibility delay). Out of scope for v1; tracked in §13. Possible v2 shape: a per-publish option (`PublishOptions.Delay`, `EnqueueOptions.Delay` — both already exist in code as outbox-only knobs; the v2 work generalizes them to direct publishers).
- **Auto-discovery of handlers** (Wolverine-style assembly scanning). Explicit registration only in v1.
- **Compat shims for the deleted `AddBusConsumer` / `AddQueueConsumer` APIs.** Greenfield — no.
- **Per-tenant or per-region topology.** Provider extension points cover this; not in this spec.
- **Source-generated registration.** Possible follow-up.
- **Bus-emulation mode for Kafka.** See section 8 — explicit future work, not this spec.
- **Reusable named topology profiles** (e.g., `services.AddBusTopology("standard", ...)` referenced by message). Possible v2 layer on top of the per-message API; not in v1.
- **Advanced per-message topology controls.** TTL, retention, dead-letter routing, sharding, encryption, per-tenant routing, large-payload chunking, subscription-filter abstractions, **ordering affinity** (`OrderingBy` — deferred; see §13) — all deferred. v1 ships three message-wide universal knobs: `MessageName`, `PartitionBy`, `CorrelationFrom` (section 4). Anything provider-specific lives in `x.UseRabbitMQ(...)` / `x.UseKafka(...)` / etc. escape hatches.

---

## 3. Registration API

Replace `services.AddBusConsumer<TConsumer, TMessage>(topic)` and `services.AddQueueConsumer<TConsumer, TMessage>(topic)` with a single message-centric fluent builder.

### Shape

```csharp
services.ForMessage<OrderPlaced>(x =>
{
    // Layer 2 — universal logical hints (see section 4)
    x.MessageName("orders.order-placed");
    x.PartitionBy(m => m.TenantId);
    x.CorrelationFrom(m => m.OrderId.ToString());

    // Layer 3 — provider escape hatch at message scope (optional)
    x.UseRabbitMQ(rmq =>
    {
        rmq.ExchangeType(ExchangeType.Topic);
        rmq.RoutingKeyFromMessage(m => $"{m.TenantId}.{m.Region}");
    });

    // Consumers
    x.OnBus<NotificationsConsumer>();
    x.OnBus<AuditLogConsumer>();

    x.OnQueue<InventoryConsumer>(q =>
    {
        q.Transport("kafka");
        q.GroupId("inventory-service");
        q.MaxConcurrency(8);
        q.Retry(3);

        // Layer 3 — provider escape hatch at per-consumer scope (optional)
        q.UseKafka(k => k.IsolationLevel(IsolationLevel.ReadCommitted));
    });
});
```

A minimal registration is still one line of useful content:

```csharp
services.ForMessage<OrderPlaced>(x => x.OnBus<NotificationsConsumer>());
```

Everything Layer 2 / Layer 3 is opt-in.

### Rules

- **Outer lambda is required.** It scopes everything message-level under a single message identity.
- **`x.MessageName(string)` is optional.** When omitted, the framework infers a message name from the message type (section 10).
- **Per-consumer lambda is optional.** `OnBus<C>()` and `OnQueue<C>()` both have a zero-arg overload for the common case, and a one-arg overload that receives the per-intent typed builder (`IBusConsumerBuilder<C>` / `IQueueConsumerBuilder<C>`).
- **Multiple consumers per intent.** Multiple `OnBus<...>` and `OnQueue<...>` calls inside the same `ForMessage<T>` block register multiple consumers for that message.
- **Same consumer in both intents.** `x.OnBus<C>(); x.OnQueue<C>();` registers the same consumer type twice; each consume invocation observes the matching `IntentType`.
- **The outer lambda returns `void`.** Calls happen at registration time; there is no chain-to-next-message on the outer builder.

### Builder hierarchy

Intent-specific builders share a base for cross-cutting options. Method names below are illustrative; final naming is a planning detail.

```
IConsumerBuilderBase<TConsumer>
├── Transport(string)              // pin to a named transport
├── Retry(int)
├── UseMiddleware<TMiddleware>()
└── ...

IBusConsumerBuilder<TConsumer> : IConsumerBuilderBase<TConsumer>
├── Subscription(string)           // bus subscription / per-subscriber identity
└── UseRabbitMQ(Action<...>)       // per-consumer provider escape hatch (binding key, args, etc.)

IQueueConsumerBuilder<TConsumer> : IConsumerBuilderBase<TConsumer>
├── Name(string)                   // queue name override
├── GroupId(string)                // consumer group for stream-style brokers
├── MaxConcurrency(int)
├── VisibilityTimeout(TimeSpan)
└── UseRabbitMQ(Action<...>) / UseKafka(Action<...>)  // per-consumer provider escape hatch
```

Provider-specific options (RabbitMQ binding key/args, Kafka isolation level, SQS visibility extensions) live as extension methods inside the appropriate `Use{Provider}` block, in the transport package.

### Transport name registry

`q.Transport("kafka")` (and the equivalent bus form `b.Transport("rabbitmq")`) pins a consumer to a **named transport**. Names are registered at host setup by provider packages:

```csharp
services.AddKafkaMessaging(k =>
{
    k.BootstrapServers = "...";
    // registers the transport under the well-known name "kafka"
});

services.AddRabbitMQMessaging(rmq =>
{
    rmq.ConnectionString = "...";
    // registers the transport under the well-known name "rabbitmq"
});
```

Each provider package owns its well-known name (`"kafka"`, `"rabbitmq"`, `"azure-service-bus"`, `"aws-sqs"`, `"nats"`). A host can also register a provider under a custom name when running multiple instances of the same broker (e.g., `services.AddKafkaMessaging("kafka-events", ...)` and `services.AddKafkaMessaging("kafka-commands", ...)` distinguish two Kafka clusters).

Resolution rules:

- **`q.Transport("foo")` with no registered transport named `"foo"` fails host startup** with a clear error naming the consumer, message type, and unresolved transport name.
- **A consumer with no `Transport(...)` call** binds to the framework's default transport policy (planning detail — likely "the first registered transport supporting the consumer's intent lane," with an explicit error when ambiguous).
- **Multiple transports registered + no default + no explicit `Transport(...)` call** fails startup; the consumer must disambiguate.

---

## 4. Intent Model

The framework defines **two logical intent lanes**:

| Lane | Semantics | Delivery |
|---|---|---|
| **Bus** | Broadcast / publish-subscribe. Every interested subscriber receives its own copy. | One-to-many. |
| **Queue** | Point-to-point / work-queue. Exactly one competing worker in the group receives each message. | One-to-one within a competing-consumer group. |

Intent lanes are **logical**. Provider mappings may differ physically (section 7). RabbitMQ maps the lanes naturally to separate exchanges. Other providers map them to topics, subjects, queues, subscriptions, or consumer groups. The framework does not assert that every provider has exactly two physical broker destinations.

### Rules

- Intent is declared at registration (`OnBus<...>` / `OnQueue<...>`) and at publish time (`IBus.PublishAsync` / `IQueue.EnqueueAsync`).
- Intent is **not** carried as a message-type marker. Domain types stay framework-agnostic.
- Intent is **not** a runtime business-behavior switch. See section 9.

### Per-message topology: three layers

Per-message topology is organized into three explicit layers. Most users live in Layer 1; intermediate users adopt Layer 2; advanced users escape into Layer 3.

| Layer | Scope | What it gives you |
|---|---|---|
| **1. No-config defaults** | The whole spec works without any topology calls. | `services.ForMessage<T>(x => x.OnBus<C>())` — message name inferred from type, provider defaults applied. |
| **2. Provider-neutral logical hints** | Universal knobs that map cleanly across transports. | `MessageName`, `PartitionBy`, `CorrelationFrom`. Each maps to a real broker primitive on most providers; behavior when a transport doesn't honor the hint is governed by `UnsupportedHintBehavior` (see below). |
| **3. Provider-specific escape hatch** | Broker-shaped knobs that only make sense for one transport. | `x.UseRabbitMQ(...)`, `x.UseKafka(...)`, `x.UseAzureServiceBus(...)`, `b.UseRabbitMQ(...)`, `q.UseKafka(...)`. Shape lives in the transport package. |

#### Layer 2 — universal knobs (the full v1 set)

All three take either a string or a `Func<TMessage, string>` selector. The selector pattern is consistent — anywhere we derive a wire-level attribute from message content, it's a `Func<TMessage, string>`.

| Knob | Purpose | Mapping rule |
|---|---|---|
| `x.MessageName(string)` | Logical message identity (override the inferred name). | Every provider has an entity-name / topic / queue-name concept; this string is what it maps to. Broker-neutral on purpose — `MessageName` reads correctly whether the message is a command (`Queue` intent) or an event (`Bus` intent), and avoids implying pub/sub semantics. |
| `x.PartitionBy(Func<T, string>)` | Partition / throughput affinity. | Kafka partition key; ASB PartitionKey; SQS message group; NATS subject sharding. Ignored on transports with no partition concept (governed by `UnsupportedHintBehavior` below). |
| `x.CorrelationFrom(Func<T, string>)` | Populate `MessagePublishOptionsBase.CorrelationId` from the message itself. | Every system has a correlation header. The selector pulls the value from a domain field (OrderId, RequestId, TraceId) so the publish-site doesn't have to thread it through options. |

##### Deferred Layer 2 knob

`OrderingBy(Func<T, string>)` — ordering affinity within a key, distinct from partitioning (ASB SessionId, SQS FIFO MessageGroupId, Pub/Sub OrderingKey). **Not in v1** — the cross-provider semantics differ materially (stateful session consumer on ASB; FIFO queue setup on SQS) and no current driver requires it. Tracked in §13.

#### Unsupported logical hints

When `PartitionBy` targets a transport that has no native partition primitive (e.g., RabbitMQ classic queues), the default behavior is **non-fatal but observable**:

- The framework emits one structured warning at host startup per `(transport, message-type)` pair — never per publish.
- A logical hint is never dropped without observability. The framework does not "silently ignore" anything.
- Per-publish runtime overhead is zero — the decision is made at startup and the no-op transport path is selected once.

In v1 this is **a single behavior** (warn at startup, continue). No configurable strictness, no enum. A future cluster — once additional Layer 2 hints land (e.g., `OrderingBy`) or real teams ask for stricter behavior — can introduce a `UnsupportedHintBehavior` enum (`Log` / `Throw` / `Ignore`) per provider. Deferred to §13.

Rationale for keeping it simple in v1: only one Layer 2 hint (`PartitionBy`) can be unsupported on one provider (RabbitMQ classic). A configurable enum on every provider package is over-built for one edge case. When the matrix grows, configurability earns its keep.

#### Provider escape hatch — invariants

Provider escape hatches (`x.UseRabbitMQ(...)`, `q.UseKafka(...)`, etc.) may refine **physical** topology — exchange types, binding keys, queue arguments, partition strategies, broker-specific options. They must obey one invariant:

> **A provider escape hatch may refine physical topology, but it must not reinterpret the logical intent lane selected by `OnBus` or `OnQueue`.**

Concretely:

- `OnQueue<C>(q => q.UseRabbitMQ(rmq => ...))` may customize the queue name, declare bindings, or set arguments — but it must not turn the consumer into a bus fanout subscriber.
- `OnBus<C>(b => b.UseRabbitMQ(rmq => ...))` may pick exchange type / routing semantics — but it must not turn the consumer into a competing worker on a single queue.
- `x.UseRabbitMQ(rmq => ...)` at message scope may set publisher-side topology (exchange type, routing-key formatter) — but it must not change which intent lane the publisher targets.

**Enforcement model.** First-party Headless provider packages (RabbitMQ, Kafka, ASB, AWS, NATS) enforce the invariant by API design: methods that would violate it are simply not in the escape-hatch API. Third-party providers are bound by the same contract but enforcement is **discipline-plus-conformance-test**, not a runtime guarantee — the conformance harness (§13.9, separate brainstorm) ships invariant tests that every provider must pass. The intent lane chosen by `OnBus` / `OnQueue` is load-bearing; escape hatches refine, they do not override.

Honest framing: the §1 phrase "structurally prevented for framework-managed paths" is shorthand for "by-construction in first-party APIs; by-contract-plus-test elsewhere." It is not a runtime invariant the framework can detect on every publish — an out-of-process admin tool issuing raw broker commands is outside framework reach.

#### Per-consumer overlay

Per-consumer config is the consume-side complement and stays where it was:

- Universal: `Transport`, `Retry`, `UseMiddleware`, `Subscription` (bus) or `Name` / `GroupId` / `MaxConcurrency` / `VisibilityTimeout` (queue).
- Provider escape: `b.UseRabbitMQ(...)`, `q.UseKafka(...)`, etc.

A consumer cannot override message-wide Layer 2 settings (`MessageName`, `PartitionBy`, `CorrelationFrom`) — those are properties of the message, not the subscriber. **This is a firm constraint in v1.** Per-consumer overrides may be reconsidered in v2 if a real driver appears (see §13.11).

#### What's NOT in v1

TTL, retention, dead-letter routing, sharding, encryption, subscription-filter abstraction, named topology profiles, `MessageData<T>` large-payload chunking. All deferred. Anything provider-specific that doesn't fit the four Layer 2 knobs lands inside a `Use{Provider}` block.

### Per-message topology: honest comparison with MassTransit

MassTransit is stronger for advanced per-message physical topology control. It allows message types and receive endpoints to customize broker-specific topology — RabbitMQ exchange names, exchange types, bindings, routing-key formatters; Azure Service Bus topic, subscription, session, and partition behavior; and so on.

Our framework intentionally starts smaller. The core model expresses **logical** messaging concerns first (intent lane, message name, partition affinity, ordering, correlation). Physical broker topology stays in provider-specific escape hatches.

Patterns absorbed from MassTransit:

- **Function-based selectors** (`Func<T, string>`) over static values — adopted across all Layer 2 knobs.
- **`CorrelationFrom`** (MassTransit `UseCorrelationId`) — pull correlation ID from the message itself.
- **Per-consumer binding override via provider escape hatch** (MassTransit `endpoint.Bind(...)`) — adopted as `b.UseRabbitMQ(rmq => rmq.BindingKey(...))`.

Patterns considered but deferred:

- **Ordering affinity** (MassTransit `UseSessionIdFormatter` for ASB; generalized to `OrderingBy` in earlier drafts) — semantically distinct enough from `PartitionBy` per provider that absorbing it requires real implementation context. Tracked in §13.

Patterns not absorbed and the reason for each are listed in section 12 (Rejected Alternatives).

### Why pick Headless over MassTransit (or Wolverine / NServiceBus / Rebus)

For most teams, MassTransit is the right answer — it is more feature-rich, more battle-tested, and has wider broker support. Headless is not trying to displace it broadly. There are three concrete situations where Headless is the better choice:

1. **You want cross-intent separation as a structural guarantee, not a discipline.** MassTransit lets endpoints reshape topology freely; a misconfigured queue subscriber can receive bus messages if someone wires it wrong. Headless makes this impossible by construction (§4 "Provider escape hatch — invariants", §5 "Routing Ownership"). If you have multiple teams adding consumers and you've been bitten by silent cross-intent bugs before, the structural guarantee earns its weight.
2. **You need cross-language interoperability on the wire.** A sibling spec defines wire compatibility with explicit constraints: no CLR type names, no .NET assembly names, no C# marker interfaces, no framework-specific envelopes. MassTransit's default envelope is CLR-shaped (URN-based `messageType` identifiers) — fine for all-.NET shops, friction for Python / Node / Go consumers. If your stack is polyglot, this matters.
3. **You are already building on the rest of Headless Framework.** The messaging slice composes with Headless's idempotency, multi-tenancy, distributed locks, observability, and configuration conventions under shared abstractions. If you've already adopted Headless for other infrastructure, picking a separate opinionated framework for messaging forces two mental models. Picking Headless.Messaging keeps the model uniform.

For the use cases Headless does not target (advanced per-message physical topology control, sagas, scheduled delivery, request/response choreography, the full MassTransit feature set), MassTransit remains the right answer. The honest comparison above is the basis of the choice, not a sales pitch.

---

## 5. Routing Ownership: Framework vs Broker

Foundational rule:

> **The broker performs routing. The framework declares topology.**

| Concern | Owner | Mechanism |
|---|---|---|
| Topology declaration (exchanges, queues, subscriptions, consumer groups, bindings) | **Framework** | Provisioned at startup from `ForMessage<T>` registrations. |
| Wire-level routing (message → queue / subscription / consumer group) | **Broker** | Exchange-to-queue bindings, topic subscriptions, consumer-group fan-out. |
| Process-level dispatch (delivered envelope → C# `IConsume<T>` instance) | **Framework** | After the broker delivers a message to a framework-managed endpoint. |

### Anti-pattern (rejected)

The framework MUST NOT implement intent routing by consuming from a shared destination and filtering on `IntentType` in-process. That would double-deliver, ignore the broker's fan-out semantics, and waste network. If a provider cannot express both intent lanes as distinct broker primitives, it declares the unsupported intent and fails startup for invalid combinations (section 6).

### What counts as a "framework-managed path"

The §1 guarantee — "cross-intent leakage is structurally prevented for framework-managed paths" — refers to a specific set of API surfaces. Knowing the boundary matters because anything outside it can violate the guarantee.

**Inside (framework-managed, guarantee holds):**

- `services.ForMessage<T>(x => ...)` registration.
- `IBus.PublishAsync(...)` and `IOutboxBus.PublishAsync(...)`.
- `IQueue.EnqueueAsync(...)` and `IOutboxQueue.EnqueueAsync(...)`.
- Consume-side dispatch from any provider package that ships under `Headless.Messaging.*`.
- Framework-driven middleware pipeline (consume + publish).

**Outside (not framework-managed, guarantee does not hold):**

- Raw `ITransport.SendAsync(...)` — the lowest-level transport API; bypasses intent registration.
- Admin / replay / migration tools that talk to brokers directly (RabbitMQ Management UI, Kafka CLI, AWS console).
- Custom code that constructs a `TransportMessage` and hands it to a transport without going through `IBus` / `IQueue`.
- Broker-side operator actions (manually binding a queue to the wrong exchange, changing routing keys, deleting subscriptions).

Teams that need to cross the boundary (replay tooling, data migrations, debugging) should do it knowingly. The framework will not catch a misuse from outside the managed surface.

### Concrete RabbitMQ shape

```
Bus intent (publish/subscribe):
  publisher -> exchange "myapp.bus" (topic) -> [broker routes by topic key]
    -> queue "order-email-service"        bound to "orders.order-placed"
    -> queue "customer-timeline-service"  bound to "orders.order-placed"

Queue intent (point-to-point):
  publisher -> exchange "myapp.queue" (direct/topic) -> [broker routes by topic key]
    -> queue "inventory-reservation"      (competing consumers share work)
```

For each provider, the framework declares the topology and publishes with the correct routing key / topic / subject. Everything downstream of publish is broker behavior.

---

## 6. Provider Capability Model

Not every transport supports every intent.

### Rules

- Each provider package **declares its supported intent lanes** as a static, compile-time capability. (Intent-lane support is a property of the provider/broker pairing; it does not vary by SKU or runtime config.)
- The framework validates registrations against the configured transport at host startup.
- **Invalid intent/transport combinations MUST fail startup with a clear error.** No silent fallback, no in-process emulation.

### Static vs runtime capability — known scope limit

Static capability covers what intent lanes a provider supports. It does **not** cover provider-specific runtime capabilities that depend on broker version, SKU, deployed plugins, or connection-time feature negotiation. Examples:

- **Azure Service Bus** sessions require Standard or Premium tier (not Basic). PartitionedEntity availability depends on namespace config.
- **NATS** JetStream availability depends on whether the JetStream subsystem is enabled at the broker.
- **RabbitMQ** Streams plugin, MQTT plugin, consistent-hash exchange plugin availability all depend on broker config.
- **Kafka** consumer-group rebalance protocols and transactional capabilities depend on broker version and client config.

Runtime-negotiated capabilities are the **provider package's responsibility** to probe at startup (after broker connection, before registration validation completes) and fail-fast on mismatch — same loud-error-at-deploy-time discipline the framework applies to intent-lane validation. A formal capability-probe contract that providers implement is an open question (§13).

### Startup validation requirements

The framework must fail fast on:

- Unsupported provider/intent combinations (e.g., `OnBus` against a queue-only transport).
- Duplicate message-name inference collisions (section 10).
- Invalid message names (per the configured naming policy).
- Missing required provider options. (Provider packages own the rules for what's required and surface their own validation errors — the framework just guarantees they run at startup, not at first dispatch.)
- Duplicate incompatible registrations for the same `(message, MessageName, intent)` triple where configuration disagrees.

Each failure surfaces as a configuration exception thrown from `IServiceProvider` build, not at first message dispatch.

---

## 7. Provider Mappings

Logical mappings only. Physical topology (number of exchanges, queues, retry queues, DLQs, bindings) still scales with registrations and provider features.

| Provider | Bus intent | Queue intent |
|---|---|---|
| **RabbitMQ** | Supported via exchange-based pub/sub (fanout or topic routing). | Supported via exchange + queue with competing consumers. |
| **Azure Service Bus** | Supported via topic + subscriptions. | Supported via queue. |
| **AWS SNS / SQS** [^aws] | Supported via SNS topic + SQS subscriptions. | Supported via SQS. |
| **NATS** | Supported via subjects. | Supported via queue groups on subjects. |
| **Kafka** | **Not supported by default.** See section 8. | Supported via topics + consumer groups. |

RabbitMQ is the strongest physical match — its exchange model maps directly onto the two intent lanes. Other providers are honest fits at the level of "this primitive plays the role of bus/queue intent for this transport."

[^aws]: AWS support in v1 covers SNS (for Bus intent) and SQS (for both Bus subscriptions and standalone Queue intent). EventBridge and Kinesis are **not** in v1 scope; they may be added as separate provider packages later.

---

## 8. Kafka Decision: Queue-Only by Default

Kafka does not have a single native broadcast primitive in the same way RabbitMQ exchanges or NATS subjects do. Kafka's topic + consumer-group model maps naturally to **durable stream processing with competing consumers within a group** — queue intent in our model.

> **Honest gap.** The canonical Kafka broadcast pattern is "one topic, N independent consumer groups" — every interested service gets its own group and sees every message. This is the dominant real-world Kafka use case for event streaming, and the v1 framework **does not support it as a first-class Bus intent**. Teams whose primary broker is Kafka and who need broadcast will either pick a different broker for Bus intent, build the per-subscriber consumer-group pattern by hand through the escape hatch, or wait for the Kafka bus-emulation brainstorm.
>
> **Why we are accepting this gap in v1.** Bus emulation on Kafka has multiple plausible shapes (consumer-group-per-subscriber, topic-per-subscriber, header-based subscription filtering, KStreams-style projection topics) with materially different operational characteristics (cost, observability, retention semantics, rebalance behavior). Picking the right shape is a deliberate design decision that deserves its own brainstorm. Quietly making the framework pick one default would lock teams into the wrong shape for their workload.

### Decision

- **Kafka provider supports Queue intent only by default.**
- **Kafka provider does not support Bus intent.** A future explicit bus-emulation mode is in scope for a separate brainstorm; out of scope here.
- An attempt to register `OnBus<...>` against Kafka **must fail startup**.

### Valid example

```csharp
services.ForMessage<OrderPlaced>(x =>
{
    x.MessageName("orders.order-placed");

    x.OnQueue<InventoryConsumer>(q =>
    {
        q.Transport("kafka");
        q.GroupId("inventory-service");
    });
});
```

### Invalid example (fails at startup)

```csharp
services.ForMessage<OrderPlaced>(x =>
{
    x.OnBus<NotificationsConsumer>(b =>
    {
        b.Transport("kafka");
    });
});
```

### Expected error wording

```
Kafka transport does not support Bus intent in v1.

The canonical Kafka broadcast pattern (one topic, N independent consumer
groups per subscriber) is not yet a first-class capability; it is being
designed in a separate brainstorm.

Workarounds for v1:
- Use OnQueue<TConsumer>() with a consumer group for point-to-point delivery.
- For broadcast-via-distinct-groups today, register one OnQueue<C>() per
  subscriber and set a unique q.GroupId(...) per consumer. The intent will
  report as Queue at the framework level, but the Kafka broker behavior
  matches the broadcast pattern.
- Or, choose a bus-capable transport (RabbitMQ, NATS, Azure Service Bus,
  AWS SNS+SQS) for Bus-intent messages.
```

A future bus-emulation mode for Kafka (e.g., unique consumer-group-per-subscriber, topic-per-subscriber, header-based filtering, KStreams projection topics) is **explicitly out of scope** for this spec and requires its own design decision. See §13.2.

---

## 9. ConsumeContext.IntentType Scope

`ConsumeContext.IntentType` is **observable delivery metadata** stamped by the framework at registration time. It is useful for:

- Diagnostics and logging (which lane delivered this message?).
- Telemetry and OpenTelemetry attribute enrichment.
- Middleware decisions (retry policy selection, idempotency keying, dead-letter routing).
- Rare shared-consumer cases where one type is intentionally registered under both intents.

### What it is not

- **Not a business-behavior switch.** If bus and queue handling differ materially, use **separate consumer types**. That is clearer, easier to test, and matches the message-centric registration shape.
- **Not a routing signal.** The broker has already routed the message by the time `IntentType` is observable; reading it does not affect dispatch.
- **Not a wire header.** Intent is registration-derived, not envelope-derived. No on-wire header carries intent (cross-language wire concerns are deferred — section 2).

### Guidance summary

| Use case | Recommended |
|---|---|
| Logging "received Bus message" vs "received Queue message" | Read `IntentType` |
| Picking a different retry policy per intent in middleware | Read `IntentType` |
| Tagging spans with `messaging.intent` | Read `IntentType` |
| Branching domain logic in a shared handler | Split into two consumer types |

---

## 10. Message-Name Inference and Validation

### Inference

When `x.MessageName(...)` is omitted, the framework infers a message name from the message type via `MessagingConventions` (`src/Headless.Messaging.Abstractions/MessagingConventions.cs`). The default transformation (planning detail) is something like `OrderPlaced` → `order.placed`.

Convention is allowed and ergonomic, but the inferred string is the contract — once a message is published under a given name, renaming the C# type is a breaking change for any consumer relying on the inferred message name.

### Collision detection

Inference is namespace-agnostic. Two different message types with the same short name will collide:

```
Sales.OrderPlaced    -> order.placed
Billing.OrderPlaced  -> order.placed
```

**Startup must fail** when two `ForMessage<T>` registrations resolve to the same message name unless both registrations explicitly opt-in to name sharing (an advanced, deliberate configuration). The default is fail-fast.

#### Uniqueness scope (local vs broker-wide)

Collision detection catches duplicates **within a single host process** — the framework can only see registrations made by code that imports `services.ForMessage<T>(...)`. Two unrelated services sharing the same broker can each register `order.placed` against incompatible payload types and the framework will accept both — the failure shows up as cross-service deserialization errors at runtime, not at startup.

Cross-service / broker-wide message-name uniqueness is **not enforced by the framework in v1**. It is the operator's responsibility until the wire-compat sibling spec (§2 Non-Goals) defines a shared schema-registry contract that brokers and services can consult. Practical recommendation in the meantime: prefix message names with the owning bounded context (`sales.order.placed`, `billing.order.placed`) rather than relying on type-name inference, so cross-service collisions are caught by code review rather than production failures.

### Collision resolution

The expected fix is to set `x.MessageName("...")` explicitly on at least one of the conflicting registrations, or rename the type. The framework error surfaces both the colliding types and the resolved message name.

### Other validation

- Message names must match the configured naming policy (provider-specific; typically lowercase, dot or hyphen separated, no whitespace).
- Empty or whitespace-only message-name strings are rejected.
- Message names exceeding provider limits (e.g., Azure Service Bus' 260-char limit on its mapped entity name) are rejected at startup, not at first publish.

### Multi-version coexistence (deferred to wire-compat sibling)

During rolling deployments or schema evolution, v1 and v2 of the same logical message commonly run simultaneously. Designing the formal versioning contract — schema name + schema version headers, side-by-side consumer registration, broker-wide name uniqueness, schema-registry interop — is part of the **wire-compatibility sibling spec** referenced in §2 Non-Goals.

Until that lands, the v1 default for teams that need coexistence is:

1. Use distinct C# types per version (`Sales.OrderPlacedV1`, `Sales.OrderPlacedV2`).
2. Set explicit `MessageName` on each registration (`x.MessageName("order.placed.v1")`, `x.MessageName("order.placed.v2")`).
3. Register both as separate `ForMessage<T>` blocks with the same or different consumers.

This is a stopgap. The version suffix in `MessageName` is a v1 expedient, not a recommended forever pattern — the sibling wire spec will likely formalize a separate `SchemaVersion` header so the logical `MessageName` stays version-free.

---

## 11. Publish / Enqueue Symmetry

Publish-side intent is explicit at the call site.

```csharp
await bus.PublishAsync(message, options, ct);     // broadcast intent
await queue.EnqueueAsync(message, options, ct);   // point-to-point intent
```

### Layout fix (#345)

- `PublishOptions` moves from `Messaging.Abstractions` → **`Bus.Abstractions`** for symmetric placement with `EnqueueOptions` in `Queue.Abstractions`.
- `MessagePublishOptionsBase` **stays in `Messaging.Abstractions`** as the shared base for both option records. No new dependency edges.
- **Transport types relocate from `Messaging.Core` → `Messaging.Abstractions`.** Today `ITransport`, `TransportMessage`, `OperateResult`, `MessageHeader`, and `BrokerAddress` live in `Headless.Messaging.Core` (`src/Headless.Messaging.Core/Transport/*`, `Messages/*`). They move into `Headless.Messaging.Abstractions` so `Bus.Abstractions` and `Queue.Abstractions` can reference them without taking a dependency on `Core`. Every transport provider package currently importing these types from `Core` must rebind its `using` directives in the same PR cluster. This widens #345's scope beyond just `PublishOptions`.
- A shared `ITransport` base is extracted in `Messaging.Abstractions`:

  ```csharp
  public interface ITransport : IAsyncDisposable
  {
      BrokerAddress BrokerAddress { get; }
      Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default);
  }

  // In Bus.Abstractions
  public interface IBusTransport : ITransport { }

  // In Queue.Abstractions
  public interface IQueueTransport : ITransport { }
  ```

  The marker subtypes preserve the package-boundary capability declaration documented in the existing transport XML docs.

### Updated package graph

```
Headless.Messaging.Abstractions
├── ConsumeContext, IConsume<T>, IntentType
├── MessagePublishOptionsBase
├── ITransport
├── TransportMessage, OperateResult, MessageHeader
└── MessagingConventions, IRetryBackoffStrategy, ...

Headless.Messaging.Bus.Abstractions   ──┐  depends on Messaging.Abstractions
├── IBus, IOutboxBus                    │
├── IBusTransport : ITransport          │
└── PublishOptions                      │
                                         │
Headless.Messaging.Queue.Abstractions ──┘
├── IQueue, IOutboxQueue
├── IQueueTransport : ITransport
└── EnqueueOptions
```

### No type-forwarding

Greenfield. The old `Headless.Messaging.Abstractions.PublishOptions` location is removed in the same PR cluster that adds the new one.

---

## 12. Rejected Alternatives

| Alternative | Why rejected |
|---|---|
| Message-type markers (`ICommand` / `IEvent`) | Pollutes domain types with framework dependencies; complicates cross-language consumers; doesn't add anything the registration-site declaration can't express. |
| Convention-based handler discovery (Wolverine-style) as the default | Adds implicit behavior; "where is this handler registered?" requires reading conventions and scanned assemblies. Explicit registration is the v1 default; discovery can be added later as opt-in. |
| "Two physical destinations always" claim for every provider | Honest only for RabbitMQ. Kafka, NATS, ASB, SQS map intent lanes to different primitives; forcing all providers into a "two destinations" shape would overpromise. |
| Cross-language wire compatibility in this spec | Real and important, but its own design space (headers, schema versioning, manifest, registry interop). Bundling it here makes this spec do four things instead of three. Sibling spec. |
| In-process intent filtering (consume from shared destination + branch on `IntentType`) | Doubles delivery, breaks broker fan-out semantics, wastes network. Routing is broker-owned. |
| Branding `IntentType` as the headline differentiator | `IntentType` is delivery metadata. Branching domain logic should be expressed as separate consumer types, not one consumer with `if/else` on intent. Demoted to "observable delivery metadata" in section 9. |
| Bus emulation on Kafka by default | Kafka has no natural broadcast primitive in the consumer-group model. Forcing an emulation (unique-group-per-subscriber, etc.) is a real design decision and deserves its own brainstorm; not a default capability. |
| Universal subscription-filter abstraction (e.g., `b.Filter("orders.*")`) | Lossy on RabbitMQ (no header-filter coverage), opaque on Kafka (no native filter). Provider-specific via `b.UseRabbitMQ(...)` is more honest for v1. |
| Per-message topology profiles (`AddBusTopology("standard", ...)` referenced by message) | Adds indirection without a v1 driver. Worth designing later as a layer on top of the per-message API once usage patterns emerge. |
| Pure fluent chain registration (no nested lambda) | Confuses return types — `OnBus<C>()` either returns the message builder (loses per-consumer config typing) or returns the consumer builder (breaks the chain). Lambda scope is clearer. |
| `Consume<C>(IntentLane lane)` uniform method instead of `OnBus` / `OnQueue` | Loses per-intent typed builders (`IBusConsumerBuilder<C>` vs `IQueueConsumerBuilder<C>`). Trades type-safety for surface symmetry; the typed builders are a clearer win. |
| Whole-doc rewrite of old `AddBusConsumer` API with type-forwarding | Greenfield project; no deployed consumers to bridge. Clean replacement is cheaper than a shim. |
| Adopt MassTransit wholesale (be a thin wrapper over MT) | Imports MassTransit's opinion stack (endpoint-centric config, per-type fanout exchanges, heavy `ConsumeContext`, CLR-shaped envelopes) which conflicts with our intent-lane model, first-class `IntentType`, and cross-language wire posture. MassTransit-as-a-provider is open as a future cluster (`Headless.Messaging.MassTransit`). |
| MassTransit-style separate `Publish<T>` / `Send<T>` topology hooks | We unified message-wide config under `ForMessage<T>`. Splitting publish vs send config conflicts with message-centric scope and forces duplicate registration sites. |
| Bus-level type formatters (`cfg.SetEntityNameFormatter(...)`) | Implicit global behavior. Our convention is explicit per-message via `x.MessageName(...)` plus a single inference rule in `MessagingConventions`. Easier to reason about; no surprise renames at the host level. |
| Naming the logical-identity knob `Topic` | Topic is broker-leaky terminology: natural for Kafka / ASB / SNS / Pub-Sub / NATS, but awkward for RabbitMQ (exchange + routing-key) and odd for queue intent (enqueueing a command, not publishing to a topic). `MessageName` is broker-neutral and reads correctly for both intent lanes. Provider-native words (Kafka `Topic`, RabbitMQ `ExchangeName`, ASB `TopicName`) stay inside the provider escape hatches where they belong. |
| `RoutingKey` as a Layer 2 universal knob | RabbitMQ-shaped concept. On Kafka / Pub/Sub / SNS it's silently no-op or coerced into a header — a footgun for users expecting cross-provider routing behavior. Lives in `x.UseRabbitMQ(rmq => rmq.RoutingKeyFromMessage(...))`. |
| MassTransit `IRoutingKeyMessage` / `IEvent` / `ICommand` marker interfaces | Same reason as our own marker-rejection above — domain types must stay framework-agnostic. |
| MassTransit `MessageData<T>` large-payload chunking | Out of scope for this spec; possible future feature when a real driver appears. |
| Disable-default-consume-topology escape (MassTransit `ConfigureConsumeTopology = false`) | Niche. Users in this territory are deep in escape-hatch land; making it a first-class flag adds surface for a small audience. Open question for v2. |
| Provider escape hatches that reinterpret intent lane | A `q.UseRabbitMQ(...)` that turned a queue consumer into a fanout subscriber (or `b.UseRabbitMQ(...)` that collapsed a bus subscriber into a competing worker) would undermine the entire intent-lane abstraction. Escape hatches refine physical topology; they do not override the lane selected by `OnBus` / `OnQueue`. Provider packages enforce this by simply not exposing methods that would violate the invariant (§4 "Provider escape hatch — invariants"). |
| Silently ignoring unsupported `PartitionBy` hints | "Silently ignored" is a footgun phrase — implies the framework hides behavior. v1 always warns once at host startup per `(transport, message-type)` pair, never per publish. No configurable strictness in v1; configurable `UnsupportedHintBehavior` enum deferred to §13. |

---

## 13. Open Questions / Follow-Up Specs

1. **Cross-language wire compatibility (sibling spec).** Required headers, schema name/version contract, JSON-default payload, optional binary serializers, manifest export, schema-registry interop. Out of scope here.
2. **Bus emulation for Kafka.** If we want Kafka to support broadcast, we need a design (per-subscriber consumer groups, topic-per-subscriber pattern, or other). Separate brainstorm.
3. **Dead-letter destination.** Probably a third intent lane (`DeadLetter`) or a per-consumer overlay. Not gating these three decisions.
4. **Reusable topology profiles.** `services.AddBusTopology("standard", ...)` referenced by `x.UseBusTopology("standard")`. Layer on top of the per-message API. Re-evaluate after v1 ships and we see whether duplication is a real problem.
5. **Per-tenant topology isolation.** Provider extension; needs its own design.
6. **Source-generated registration scanning** (Wolverine-style discovery as opt-in additive).
7. **Pluggable serializers per message** (`x.Serializer(...)` — JSON UTF-8 is the v1 default, but binary formats need a story).
8. **`PartitionBy` semantics on multi-transport publishes.** Resolved in §4 — per-provider `UnsupportedHintBehavior` knob (`Log` default, `Throw` for strict environments, `Ignore` for explicit suppression). Remaining open detail: do we expose the diagnostic events via an `IObservable<MessagingDiagnostic>` for tooling integration, or stay logger-only for v1?
13. **`OrderingBy` as a Layer 2 knob.** Deferred from v1 (see §4 "Deferred Layer 2 knob"). Ship as a follow-up when a real driver appears — most likely paired with the dead-letter destination work or a first ASB / SQS-FIFO provider implementation that needs sessions or FIFO groups. Open design question: is it a single `OrderingBy(Func<T,string>)` knob, or do we split into provider-named knobs (`SessionBy` for ASB, `GroupBy` for SQS FIFO) given the divergent broker semantics?
14. **`UnsupportedHintBehavior` configurability.** v1 ships a single behavior (warn at startup, continue) for unsupported `PartitionBy` hints. When the Layer 2 matrix grows beyond `PartitionBy`, or when production-strict teams ask for fail-fast, introduce the per-provider `Log` / `Throw` / `Ignore` enum. Scoped per-provider in the original design — preserve that when it lands.
15. **Provider runtime capability probe contract.** §6 splits capability into static (intent lanes — framework-validated) and runtime-negotiated (sessions, JetStream, plugin availability — provider-validated). The current contract is "provider's responsibility, do it at startup." A formal contract — e.g., `IProviderCapabilityProbe.ValidateAsync(ProviderCapabilityContext ctx)` — would let the framework orchestrate probe ordering and surface a uniform diagnostic. Not needed in v1 with two provider packages; revisit when third-party providers appear.
16. **Request / response (RPC) messaging.** Send-and-await-correlated-reply pattern. Possible v2 shape: a third intent lane (`Rpc`), or a wrapper over Queue intent with framework-managed correlation and a per-instance reply channel. Separate brainstorm; needs decisions on reply-channel lifetime, timeout semantics, fanned-out vs single-reply.
17. **Scheduled / delayed delivery.** Publishing a message with a future delivery time. `PublishOptions.Delay` and `EnqueueOptions.Delay` already exist in code as outbox-only knobs (only honored when persisted through `IOutboxBus` / `IOutboxQueue`). v2 work: generalize to direct publishers where the provider has a native delay primitive (RabbitMQ delayed-message exchange, ASB scheduled enqueue, SQS visibility delay), and define the unsupported-provider behavior (probably the same warn-at-startup pattern as `PartitionBy`).
9. **Conformance test harness (separate brainstorm, not v1).** `Headless.Messaging.Testing.Harness` could ship an `ITransportConformanceTest` base class that every provider package consumes (capability declaration, startup validation, two-lane separation, dispatch correctness, partition function honoring, escape-hatch invariant enforcement). Several findings in this cluster would benefit from it (capability drift, escape-hatch invariant enforcement, intent-lane separation). **Not gated by #341/#344/#345.** v1 covers correctness with per-provider integration tests; the harness is a separate brainstorm once we have 2+ provider implementations to pull common patterns from.
10. **PR cluster split.** #341, #344, #345 land together; wire compat lands as a sibling cluster; per-message advanced topology (TTL, retention, DLQ) lands as a third cluster as needed.
11. **Per-consumer override of Layer 2 knobs.** Should a specific consumer be able to override `OrderingBy` / `CorrelationFrom` for its own subscription (e.g., "this audit consumer correlates by `m.AuditId` instead of `m.OrderId`")? Lean: no — message-wide settings are properties of the message and consumers see the same value. Revisit if a real driver appears.
12. **`Headless.Messaging.MassTransit` provider.** Wrap MassTransit as a transport behind our `IBusTransport` / `IQueueTransport`. Lets users with MassTransit-only transports (or migrating off MassTransit) plug in without rewriting consumers. Separate cluster after v1.

---

## References

- Existing code:
  - `src/Headless.Messaging.Abstractions/IConsume.cs` — handler contract
  - `src/Headless.Messaging.Abstractions/ConsumeContext.cs` — `IntentType` property at line 256; **rename `Topic` property → `MessageName`** as part of this cluster
  - `src/Headless.Messaging.Abstractions/PublishOptions.cs` and the shared `MessagePublishOptionsBase` — **rename `Topic` property → `MessageName`** as part of this cluster (the publish-side override must match the registration-side knob name)
  - `src/Headless.Messaging.Abstractions/IntentType.cs` — enum definition
  - `src/Headless.Messaging.Abstractions/MessagePublishOptionsBase.cs` — shared base
  - `src/Headless.Messaging.Abstractions/PublishOptions.cs` — **moves to Bus.Abstractions**
  - `src/Headless.Messaging.Queue.Abstractions/EnqueueOptions.cs` — stays
  - `src/Headless.Messaging.Bus.Abstractions/` — currently an **empty scaffold** (only `bin/` and `obj/`; no `.cs` files). This cluster populates it by creating `IBusTransport.cs` (marker subtype of `ITransport`) and migrating `IBus` and `IOutboxBus` contracts out of `Messaging.Abstractions`.
  - `src/Headless.Messaging.Queue.Abstractions/` — same status: **empty scaffold**. This cluster populates it with `IQueueTransport.cs` and migrates `IQueue` and `IOutboxQueue` contracts out of `Messaging.Abstractions`.
  - `src/Headless.Messaging.Core/ServiceCollectionExtensions.cs` — **`AddBusConsumer` / `AddQueueConsumer` deleted; `ForMessage<T>` added**
  - `src/Headless.Messaging.Abstractions/MessagingConventions.cs` — message-name inference source (rename `GetTopicForType<T>()` → `GetMessageNameForType<T>()` as part of this cluster)

- Related brainstorms:
  - `docs/brainstorms/2026-05-19-messaging-middleware-pipeline-requirements.md` — middleware pipeline (#218, landed)
  - `docs/brainstorms/2026-05-18-messaging-adopt-distributed-lock-provider-requirements.md` — concurrency model

- Issues:
  - #341 — consumer-model evolution
  - #344 — broadcast cluster cross-intent leakage
  - #345 — abstractions polish layout
