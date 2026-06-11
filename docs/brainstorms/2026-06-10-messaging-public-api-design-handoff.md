# Handoff Spec — Messaging Public API & Intent Model

**Created:** 2026-06-10
**Status:** Brainstorm / design exploration — no decision locked. **Self-contained handoff** — written for a cold-start agent (see §0.1).
**Author:** Shaheen (with Claude)
**Related:** #359 (dual-lane physical topology), #344 (cross-intent leakage), #217 (consumer-model roadmap), `docs/plans/2026-06-10-001-feat-messaging-dual-lane-topology-kafka-guard-plan.md`

---

## 0. Why this document exists (the handoff)

Three threads converged in one conversation and deserve a durable home before the next session picks them up:

1. **#359** is about to physically separate the Bus and Queue lanes per provider.
2. **#344** is the design ticket #359 closes — the *cross-intent leakage* bug.
3. While studying both, a deeper question surfaced: **is our late-bound intent model (`OnBus`/`OnQueue` per registration, decoupled from message type) the right foundation — and what should the public registration/publish API look like for the long term?**

This doc captures the current API precisely, compares it to the major .NET messaging frameworks, and brainstorms a well-designed public API. **It is a thinking artifact, not a plan.** The next session should read it, pick a direction on the central fork (§7), then spin a real requirements doc + plan.

**The central fork is unresolved and is the owner's call.** Everything downstream (API shape, how #359 is narrated, whether the conformance machinery is universal or escape-hatch-scoped) flows from it.

### 0.1 How the next agent should use this document

**You are likely picking this up cold.** Read in this order: §1 (why the problem exists) → §2 (what we have) → §3 (what others do) → §4 (what's wrong) → §5–§7 (the options and the fork) → Appendix A (code map — where everything lives) → Appendix C (what to actually build per option).

**Before you trust any `file:line` anchor, re-verify it.** This doc was written 2026-06-10 against `main`; code drifts. Every anchor in Appendix A was accurate at authoring time — confirm with `grep`/`Read` before acting on it. Treat drift as expected, not as the doc being wrong. *Update (2026-06-10, second session): all Appendix A anchors re-verified against the working tree — correct as written. New mechanism-level evidence in §3.2; modeled target API in Appendix F.*

**Decision state (read Appendix D for the full ledger):**
- **DECIDED, do not reopen:** #344 → physical separation (Option 1 of #344). #359 plan is written and ready to execute.
- **DECIDED (2026-06-10, second session, owner's call): §7 central fork → Option 3 — full type=intent, no dual-lane escape hatch.** Q7 resolved (intent on the type; no primitive split) and Q6 resolved (no escape hatch — dual-lane needs are modeled as two types). Rationale: evidence showed zero internal dual-lane demand (§3.2 E5), the outbox collapse is already half-built (§3.2 E1), and Option 3 is the simplest industry-aligned end-state — the #344 collision becomes unrepresentable.
- **DECIDED (2026-06-10, second session, owner's call, after DX deep-dive): Q1 / F.3-a → A1 — marker interfaces (`IEvent`/`ICommand` over a common `IMessage`) + generic constraints on the publish/consume surfaces.** Wrong verb and unmarked types = compile errors. Conventions and builder classification are deleted (structurally dead under constraints); unowned types are wrapped (anti-corruption doctrine). Residual both-markers hole = boot check (F.3-f).
- **STILL OPEN (feed the requirements doc):** F.3 sub-decisions b–e (publisher naming, options unification, no-ambient-tx behavior, `Delay` on direct), Q3 (discovery as default), Q4 (fail-closed header check), Q5 (cleanest path from `OnBus`/`OnQueue`).
- **OUT OF SCOPE:** migration/upgrade paths (greenfield — no deployed consumers).

**Your job is probably one of these — confirm with the owner which:**
1. **Help resolve the fork** — deepen §5–§7, prototype the API shapes in Q7, produce a recommendation. Output: a `/x-brainstorm` requirements doc.
2. **Execute a chosen option** — once the owner picks, write the plan (`/x-plan`) and implement. Appendix C tells you the work per option.
3. **Do the fork-independent work** — file/execute Q4 (`_ValidateIntentHeader` fail-closed) and the §359 plan, which are correct under every branch.

**Hard rules:** Do **not** block or alter the #359 *code* on this fork — only its *narration*. Do **not** invent product behavior the owner hasn't chosen. When in doubt between options, present, don't decide.

### 0.2 Related artifacts

- **#359 plan** (ready to execute): `docs/plans/2026-06-10-001-feat-messaging-dual-lane-topology-kafka-guard-plan.md`. Physically separates Bus/Queue per provider (RabbitMQ exchange split, NATS retention-policy split, Pulsar topic split), adds the Kafka §8 startup hint, Redis durability docs, and a cross-provider lane-isolation conformance test. Ships regardless of the §7 fork.
- **#344** — design ticket, DECIDED (physical separation). #359 closes it on merge.
- **#217** — consumer-model evolution roadmap (Cluster 0). Clusters 0.3/0.4 shipped the `ForMessage<T>` builder + `OnBus`/`OnQueue` + provider escape hatches. This doc questions whether that model is the right long-term foundation.
- **Origin spec is gone:** the `2026-05-25-…requirements.md` / `2026-05-26-001-…plan.md` referenced by #359/#344 were superseded and deleted. The issue bodies + this doc are now the authoritative record.

---

## 1. Background: the leak and its model roots

### 1.1 What #344 found

During the adversarial review of **PR #340** (the PR that *introduced* the bus/queue intent split), a reviewer found: when the same logical message name is published as both Bus and Queue intent, **both** subscriber types receive **both** messages — the transport routes intent-blindly. Audited on RabbitMQ; the pattern repeats across providers.

Root cause: producer-side intent (`IntentType.Bus` / `IntentType.Queue`) drives the framework's *internal routing* (which dispatcher claims the row, which capability fence applies) but was **not stamped into the broker topology**. One exchange, two lanes bound to it → leakage.

### 1.2 #344's decision

`DECIDED` (2026-05-26): **Option 1 — physical separation per provider.** Bus → fanout/topic semantics; Queue → point-to-point. The two other options were rejected:
- *Option 2 — header check + nack on mismatch.* (Partially built: see §1.3.)
- *Option 3 — producer-convention contract.* (No enforcement; rejected.)

### 1.3 Current state: "detect, don't prevent"

Finding #12 from that review is **live in the codebase today**:
- Publish stamps `headless-intent: Bus|Queue` into every message — `src/Headless.Messaging.Core/Internal/IMessagePublishRequestFactory.cs:87`, header constant `Headers.Intent` (`src/Headless.Messaging.Abstractions/Headers.cs:123`).
- Consume validates it — `ConsumeMiddlewarePipeline._ValidateIntentHeader` (`:454`) — **but only logs a warning** (`ConsumeIntentMismatch`) on mismatch. No nack, no drop.

So the leak *happens* today and you get a log line. **#359 (physical separation) is what makes it stop happening.** The intent header + validator is Option 2's substrate sitting behind Option 1 as latent defense-in-depth.

### 1.4 The meta-observation

All three of #344's options are *containment strategies for a problem the model invites*. None asked the fourth question — **should one message name be allowed on both intents at all?** MassTransit and NServiceBus answer "no" by binding intent to message type, so the collision is unrepresentable and there is nothing to contain. #344 is the cleanest answer to a question that only exists because intent is late-bound.

---

## 2. Current public API (documented precisely)

### 2.1 Registration — message-centric

The root of registration is the **message**, not the consumer:

```csharp
services.AddHeadlessMessaging(setup =>
{
    setup.Options.RetryPolicy.MaxPersistedRetries = 15;
    setup.UseSqlServer("conn");
    setup.UseRabbitMq(r => r.HostName = "localhost");

    setup.ForMessage<OrderPlaced>(m => m
        .MessageName("orders.placed")
        .CorrelationFrom(o => o.OrderId)
        .OnBus<AuditConsumer>(c => c.Group("audit").Concurrency(4))
        .OnQueue<FulfillConsumer>(c => c.Group("fulfill").WithCircuitBreaker(cb => …)));

    setup.ForMessagesFromAssembly(typeof(OrderPlaced).Assembly); // scan → all BUS intent
});
```

Key types (all `[PublicAPI]`):
- `ForMessage<TMessage>(Action<IMessageBuilder<TMessage>>)` — entry point (also a service-collection twin for library code).
- `IMessageBuilder<T>` — `MessageName(string)`, `CorrelationFrom(Func<T,string?>)`, `OnBus<C>()`, `OnBus<C>(configure)`, `OnQueue<C>()`, `OnQueue<C>(configure)`.
- `IConsumerBuilderBase<C,TBuilder>` (lane-specific via `IBusConsumerBuilder<C>` / `IQueueConsumerBuilder<C>`) — `Group(string)`, `Concurrency(byte)`, `HandlerId(string)`, `WithCircuitBreaker(...)`. Provider escape hatches (`UseKafka`, `UseRabbitMq`, …) extend these per Cluster 0.4.
- `ForMessagesFromAssembly[Containing]` — discovery, but **defaults every scanned consumer to Bus intent** (intent cannot be inferred by the scanner).

### 2.2 Consume contract

```csharp
public sealed class OrderPlacedHandler(IOrderRepository orders) : IConsume<OrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken ct) => …;
}
```

`ConsumeContext<T>` carries `Message`, `IntentType` (the lane that delivered it), `TenantId`, correlation, headers. `IntentType` is the *only* signal that the same handler shape could be hit by either lane.

### 2.3 Publish — four interfaces

| Interface | Lane | Transaction | Method |
|---|---|---|---|
| `IBus` | broadcast | direct | `PublishAsync<T>(content, PublishOptions?, ct)` |
| `IQueue` | point-to-point | direct | `EnqueueAsync<T>(content, EnqueueOptions?, ct)` |
| `IOutboxBus` | broadcast | outbox | `PublishAsync<T>(…)` |
| `IOutboxQueue` | point-to-point | outbox | `EnqueueAsync<T>(…)` |

**Intent is chosen at the call site** (which interface + method) and is **independent** of the consumer registration's intent. Nothing ties `IBus.PublishAsync<OrderPlaced>` to the existence of an `OnBus<…>` registration for `OrderPlaced`. That independence *is* the #344 leak surface.

---

## 3. How the major frameworks register & publish

| Framework | Registration anchor | Intent binding | Common-case verbosity | Publish surface | Cross-intent leak possible? |
|---|---|---|---|---|---|
| **Headless (today)** | **Message** (`ForMessage<T>`) | **Late-bound** per registration & per publish call | High — name + consumer + intent | **4 interfaces** (Bus/Queue × direct/outbox) | **Yes — by design** |
| **MassTransit** | Consumer (`AddConsumer<T>`) | **Type** (event→Publish, command→Send) | Low — `AddConsumer<T>()` + `ConfigureEndpoints()` | `IPublishEndpoint` / `ISendEndpoint` / `IBus` | No (type=intent) |
| **NServiceBus** | Handler (auto-discovered) + routing | **Type** (`IEvent`/`ICommand`, *enforced* — throws) | Lowest — implement handler, conventions route | `IMessageSession` (`Send`/`Publish`) | No (type=intent, enforced) |
| **Wolverine** | Handler (convention discovery) | **Routing rules** (`PublishMessage<T>().ToX()`) + local conventions | Lowest — define `Handle(T)`, discovered | **1** `IMessageBus` (routing decides) | No (a message routes per its rules) |
| **Rebus** | Handler (`IHandleMessages<T>`, assembly-scanned) | **Verb + type-based routing** (`Send`→mapped queue, `Publish`/`Subscribe`→type-topic) | Lowest — implement `IHandleMessages<T>`, auto-registered | **1** `IBus` (`Send`/`Publish`/`SendLocal`) | No (Send and Publish resolve to different destinations) |
| **Foundatio** | **Primitive** (inject `IMessageBus` *or* `IQueue<T>`) | **Separate primitives** — broadcast vs work-queue are different abstractions | Low — `SubscribeAsync` / `StartWorkingAsync` | **2 separate primitives** (`IMessageBus`, `IQueue<T>`) | **No — no shared topology** (different subsystems) |
| **CAP** (our origin) | Method (`[CapSubscribe("name")]`) | None (publish/subscribe only) | Low — `[CapSubscribe]` attribute | `ICapPublisher` | N/A (no intents) |

Sources: [MassTransit Consumers](https://masstransit.io/documentation/configuration/consumers) · [MassTransit Topology](https://masstransit.io/documentation/configuration/topology) · [NServiceBus Messages/Events/Commands](https://docs.particular.net/nservicebus/messaging/messages-events-commands) · [Wolverine Message Routing](https://wolverinefx.net/guide/messaging/subscriptions.html) · [Rebus Routing](https://github.com/rebus-org/Rebus/wiki/Routing) · [Foundatio IMessageBus](https://github.com/FoundatioFx/Foundatio/blob/main/src/Foundatio/Messaging/IMessageBus.cs) · [Foundatio IQueue](https://github.com/FoundatioFx/Foundatio/blob/main/src/Foundatio/Queues/IQueue.cs).

### 3.1 The three camps — and where Headless sits

Strip away surface differences and every framework except Headless lands on **one invariant: a given message type resolves to exactly one lane / destination.** They express it three ways:

- **(a) Type-tagged on a unified bus** — MassTransit, NServiceBus. The *type* says event-or-command; one bus enforces it (NServiceBus *throws* on misuse). CAP is the degenerate case (pub/sub only — everything is an "event").
- **(b) Verb + type-routing** — Wolverine, Rebus. One `IBus`/`IMessageBus`; you pick `Send` vs `Publish`, and routing rules / type→destination maps resolve where it goes. Send-commands and Publish-events land on different destinations by construction, so they can't cross.
- **(c) Separate primitives** — Foundatio. Broadcast (`IMessageBus`) and work-queue (`IQueue<T>`) are *different subsystems* with different implementations, keyed by the work-item type. You pick the lane by *which primitive you inject*. No shared topology → leakage is structurally impossible, the same way type=intent makes it unrepresentable.

★ The Headless tell ─ Headless already exposes **Foundatio-style separate publish primitives** (`IBus` / `IQueue`) — but unlike Foundatio it backs them with a **single unified registration (`ForMessage<T>`) and a shared transport/storage/dispatch pipeline**, *and* unlike the type-tagged camp it lets the **same type** register `OnBus` *and* `OnQueue`. That hybrid — separate front door, shared plumbing, one type on both lanes — is *exactly* where #344 lives. Foundatio avoids the leak by separating all the way down; MassTransit/NServiceBus by tagging the type; Wolverine/Rebus by routing each verb to a distinct destination; Headless does none of these, so it has to manufacture isolation (#359) and test for it. **Headless is the only one of the seven that lets one type traverse both lanes.** That is the outlier, and the tax.

### 3.2 Deep-dive evidence (researched + code-verified 2026-06-10, second session)

A second pass re-verified every Appendix A anchor (all correct) and pulled mechanism-level evidence on the concerns that decide the API shape.

**E1 — Outbox: the industry converged on ambient policy, and our code is already half-way there.**
- MassTransit (`AddEntityFrameworkOutbox` + `UseBusOutbox`), NServiceBus (`EnableOutbox()`), and Wolverine (`Policies.AutoApplyTransactions()`) all keep the publish verb unchanged and intercept; the caller never chooses outbox-vs-direct at the call site. The only explicit-interface precedent is Wolverine's `IDbContextOutbox` — and it exists solely for code *outside* the handler pipeline, with the same `PublishAsync` verb (only the receiver object changes).
- Headless's own `OutboxMessageWriter` (`src/Headless.Messaging.Core/Internal/OutboxMessageWriter.cs:81–147`) **already branches on `IOutboxTransactionAccessor.Current`** (async-local ambient transaction): tx present → message row persisted in the same `DbTransaction`, buffered, dispatched post-commit; tx absent → stored standalone, then enqueued. The ambient mechanism Q2 asks about exists; what's missing is only collapsing the four facades over it.
- Quirk: `PublishOptions.Delay` / `EnqueueOptions.Delay` is honored **only by the outbox pair** and silently ignored by direct `IBus`/`IQueue`. A collapsed surface must resolve this (scheduling requires storage: auto-route delayed messages via outbox, or throw on direct).
- `PublishOptions` and `EnqueueOptions` are structurally identical (`MessageOptions` base + `Delay`) — the four-interface split carries no real option divergence.

**E2 — Type-level intent: no industry convergence; NServiceBus is the only enforcement precedent.**
- NServiceBus: markers (`IEvent`/`ICommand`) **or** unobtrusive predicates (`conventions.DefiningEventsAs(type => …)`); runtime-enforced (`Publish` on a command throws *"Pub/Sub is not supported for Commands. They should be sent directly to their logical owner."*) and disablable. Lesson: enforcement must ship with markers *and* conventions, plus an off switch.
- MassTransit: verb-conveyed only (`Publish` vs `Send`), zero type enforcement; publishing a "command" type silently fans out (or is broker-discarded with no subscribers).
- Wolverine: intent lives in routing rules; `SendAsync` throws on no route, `PublishAsync` silently no-ops. All three treat publish-with-no-subscribers as a non-error.
- **C# reality check:** compile-time enforcement (`where T : IEvent`) and unobtrusive conventions are mutually exclusive on the same method — constraints don't participate in overload resolution, so a convention-classified POCO can't flow through a marker-constrained overload. The realistic enforcement ladder: runtime check always → markers for documentation + optional Roslyn analyzer for compile-time feel. (This corrects §5 Option 2's "COMPILE ERROR" sample — that's only achievable by mandating markers and foreclosing unobtrusive mode.)

**E3 — Discovery + per-consumer config: scan is table stakes; config never lives at the message-handling call site.**
- All of MT/NSB/Wolverine scan-discover. Granularity precedents: NServiceBus = per-endpoint only; Wolverine = queue-level + `[StickyHandler]`; MassTransit = `ConsumerDefinition<T>` co-located class — the most-copied shape for per-consumer config that survives discovery.
- Headless's `ForMessage<T>` block is already a strong answer (message-centric co-location, §4 strengths). Discovery-by-default with `ForMessage<T>` as the override matches the converged principle exactly.

**E4 — Kafka capability honesty: the mature precedent is structural, not runtime.**
- MassTransit makes Kafka a *Rider*: separate noun (`ITopicProducer<T>`), separate verb (`Produce`), mandatory consumer group, **unreachable from `IPublishEndpoint.Publish`**. The compile-time absence of a path from the bus API to Kafka *is* the guard. Validates B.2; #359 U5's runtime guard is the right interim behavior.

**E5 — Code facts that move the fork (verification pass):**
- **Dual-lane same-type registration is exercised by exactly one unit test** (`ForMessageRegistrationTests.should_register_same_consumer_under_bus_and_queue_lanes`) **and used by nothing else in the repo.** The only internal framework consumer of messaging (Caching.Hybrid's invalidation consumer) is assembly-scanned onto the Bus lane. Direct evidence for Q6: the capability the escape hatch preserves has zero internal demand.
- **Option 4's un-sharing cost is confirmed very high:** storage (one table schema, `IntentType` discriminator), `Dispatcher`, `ConsumeMiddlewarePipeline`, `MessageRegistry`/registration model, and the publish pipeline are all lane-shared. Only the transport interfaces (`IBusTransport`/`IQueueTransport`) and the thin `IBus`/`IQueue` facades are separate. Splitting the primitives means duplicating or forking all five shared components; #359 pays only the topology part.
- **`ConsumeContext.IntentType` is registration-derived, not wire-derived** — a handler registered `OnBus` sees `Bus` even if the wire header says `Queue`; the wire header feeds only the log-only mismatch warning (Q4's target).
- **Scanned consumers hardcode Bus** (`ScannedConsumerBuilder.cs:62`) — W3 confirmed at code level.
- Internal registration is keyed by `(MessageName, Group, IntentType, ConsumerType)` (`Setup.cs:602–624`); the model is already name+intent-keyed, so re-keying intent onto the type is a registration-layer change, not a storage-layer one.

---

## 4. Diagnosis — what our API gets right and wrong

### Strengths (keep these)
- **Message-centric co-location.** `ForMessage<T>` puts name, correlation, every consumer, and provider knobs in one block. Answers "what happens to `OrderPlaced`?" at a glance — genuinely nicer than MassTransit's scattered `ConsumerDefinition`s for that question.
- **Symmetric, legible verbs.** `OnBus`↔`IBus.PublishAsync`, `OnQueue`↔`IQueue.EnqueueAsync`. Consistent mental model.
- **Compile-time consumer typing.** `IConsume<T>` + `ConsumeContext<T>` are clean and fast.

### Weaknesses (the design debt)
- **W1 — Intent is a free-floating second axis.** Declared independently on publish (which interface) and consume (`OnBus`/`OnQueue`), with nothing reconciling them except runtime physical separation (#359) + a log-only header check. This is the root of #344.
- **W2 — Verbose common case.** The 95% case (one handler for one event) costs a full `ForMessage<T>(m => m.OnBus<H>())`. Every competitor makes this near-zero-config.
- **W3 — Discovery can't express intent.** `ForMessagesFromAssembly` defaults everything to Bus — scanning and the intent model are at odds.
- **W4 — Four publish interfaces.** Bus/Queue × direct/outbox is a wide surface. Wolverine ships one `IMessageBus`; outbox is a policy, not a type.
- **W5 — AI-legibility.** An agent generating code must independently reason about lane at every publish call and every registration — more surface for silent, uncaught error. Type-bound intent would make most mistakes compile errors.

---

## 5. Brainstorm — four coherent API directions

> These map onto the intent-model fork. Each is internally consistent; they are not à-la-carte. Options 2–4 all honor the industry invariant (one type → one lane); they differ in *how* it's expressed.

### Option 1 — Keep late-bound, **unify** the two intent axes (minimal change)

Keep `OnBus`/`OnQueue` and the four publish interfaces, but make publish-intent **derive from / validate against** the registration so you cannot publish a message on a lane it has no consumers for. Promote `_ValidateIntentHeader` from log-only to configurable fail-closed.

```csharp
// Registration unchanged. Publish validated:
await bus.PublishAsync(new OrderPlaced(…));   // OK only if OrderPlaced has an OnBus registration
await bus.EnqueueAsync(new OrderPlaced(…));   // throws/blocked if no OnQueue registration exists
```

- **Pro:** smallest break; keeps flexibility; closes the *accidental* leak; #359 stays exactly as planned.
- **Con:** still two axes, still the cognitive double-load, still diverges from industry norms; doesn't help W2/W4/W5. Treats the symptom.

### Option 2 — **Type=intent default + dual-lane escape hatch** (recommended lean)

Intent lives on the message **type** by default; the rare genuinely-dual-lane message opts in explicitly — and *that* is the only place #359's isolation machinery is needed.

```csharp
// Default: intent on the type. Convention OR marker OR builder declaration — see Q1.
public record OrderPlaced(…) : IEvent;        // broadcast
public record ProcessPayment(…) : ICommand;   // point-to-point

public sealed class AuditHandler : IConsume<OrderPlaced> { … }     // no intent here — it's on the type

services.AddHeadlessMessaging(setup =>
{
    setup.UseSqlServer("conn").UseRabbitMq("localhost");
    setup.AddConsumersFromAssemblyContaining<OrderPlaced>();        // discovery; intent from type
    setup.ForMessage<OrderPlaced>(m => m                            // explicit override still available
        .Name("orders.placed")
        .CorrelationFrom(o => o.OrderId)
        .Consumer<AuditHandler>(c => c.Group("audit").Concurrency(4)));
});

// One bus; the type enforces the verb:
await bus.PublishAsync(new OrderPlaced(…));     // compiles
await bus.PublishAsync(new ProcessPayment(…));  // COMPILE ERROR — it's a command; use SendAsync
await bus.SendAsync(new ProcessPayment(…));     // compiles
// outbox vs direct = ambient transaction / option, not a separate interface

// Escape hatch — the rare legit dual-lane message; #359 isolation applies HERE only:
public record InventoryChanged(…);              // intentionally no marker
setup.ForMessage<InventoryChanged>(m => m
    .AsDualLane()
    .OnBus<DashboardProjector>()                 // explicit lane because the type doesn't carry it
    .OnQueue<ReorderWorker>());
```

- **Pro:** 95% case is compile-safe + terse (matches MT/NSB/Wolverine); collapses W2/W4/W5; #359's machinery becomes the escape-hatch substrate, **not wasted**; #344 reframes from "a leak we fixed" to "the isolation guarantee for opted-in dual-lane messages."
- **Con:** introduces a type-level intent declaration (marker/convention/attribute) — mild tension with "zero lock-in" (see Q1 for keeping POCOs clean); two registration styles to document; collapsing outbox/direct into a policy needs careful transactional-visibility design.

### Option 3 — **Full type=intent** (max convergence, biggest break)

Drop late-bound intent entirely. Every message is an event or a command; no dual-lane escape hatch. #359's per-provider isolation work becomes **unnecessary long-term** (the collision is unrepresentable) — though still worth shipping now as a correctness net during transition.

- **Pro:** simplest end-state; fully industry-aligned; maximal AI-legibility; deletes a whole class of machinery.
- **Con:** largest break from shipped Clusters 0.3/0.4; removes a capability some users may rely on; arguably *makes #359 partly throwaway*.

### Option 4 — **Separate primitives (Foundatio-style)** — intent *is* the primitive

Lean into the model Headless half-implements already. Keep `IBus`/`IQueue` as genuinely separate primitives, key each by message type, and let the *choice of primitive* be the intent. No marker interface, no `OnBus`/`OnQueue` flag — you register a **bus consumer** or a **queue worker**, and you inject `IBus` or `IQueue`. The lanes share no message identity, so #344 is structurally impossible (Foundatio's guarantee), and it fits Headless's existing Foundatio-like building-blocks identity *without* adding type markers.

```csharp
// A type is a bus message or a queue work-item by WHICH primitive registers/handles it — not a tag on the type.
public sealed class AuditHandler : IConsume<OrderPlaced> { … }     // registered as a bus consumer
public sealed class FulfillWorker : IConsume<FulfillOrder> { … }   // registered as a queue worker

services.AddHeadlessMessaging(setup =>
{
    setup.UseSqlServer("conn").UseRabbitMq("localhost");
    setup.Bus.ForMessage<OrderPlaced>(m => m.Name("orders.placed").Consumer<AuditHandler>());
    setup.Queue.ForMessage<FulfillOrder>(m => m.Consumer<FulfillWorker>(c => c.Concurrency(8)));
});

await bus.PublishAsync(new OrderPlaced(…));     // IBus — broadcast subsystem
await queue.EnqueueAsync(new FulfillOrder(…));  // IQueue — work subsystem (separate topology end-to-end)
```

- **Pro:** structural leak-prevention like Foundatio (no shared plumbing to leak through); no type markers (keeps the "zero lock-in" identity cleaner than Option 2); aligns with Headless's existing `IBus`/`IQueue` surface and its Foundatio-like sibling subsystems (caching, locks, blobs); the registration *namespace* (`setup.Bus.` / `setup.Queue.`) makes intent obvious and discoverable.
- **Con:** the biggest *internal* refactor — it means **un-sharing** the transport/storage/dispatch pipeline that the two lanes currently co-use, which is most of what #359 carefully separates at the topology layer. Pushed to its conclusion, #359's physical split is the *first step* of this; the rest is separating the framework-internal pipeline too. Also forecloses the rare dual-lane case more firmly than Option 2's escape hatch (you'd define two types). *(Cost confirmed very high by the 2026-06-10 verification pass — storage, dispatcher, consume pipeline, registry, and publish pipeline are all lane-shared; see §3.2 E5.)*

---

## 6. Recommendation (lean, not decision)

**Lead with Option 2; treat Option 4 as the close runner-up worth a serious look** given Headless's Foundatio-like identity.

- **Option 2** matches how durable frameworks evolve — an opinionated default plus a deliberate escape hatch — and is the only option where **#359 is strictly not wasted**: physical separation becomes the dual-lane implementation rather than a universal tax. Best serves "AI-first, clean API": intent self-documents in the type, most mistakes are compile errors, publish surface collapses toward one bus.
- **Option 4** is arguably *more native* to this framework — Headless already ships separate `IBus`/`IQueue` primitives and a building-blocks philosophy lifted straight from Foundatio. It prevents the leak *structurally* with no type markers, sidestepping Option 2's mild "zero lock-in" tension. Its cost is the larger internal refactor (un-sharing the pipeline), and #359 is its down payment, not its waste.

**Evidence update (2026-06-10 second session, §3.2):** three findings move this lean. (1) The outbox collapse Option 2 wants is already half-built — `IOutboxTransactionAccessor` + the `OutboxMessageWriter` ambient branch exist; the four publish interfaces are facades over one writer. (2) Dual-lane same-type registration has **zero internal usage** (one unit test only) — weakening the escape hatch's claim and making Option 3 more credible than first assessed. (3) Option 4's un-sharing cost is confirmed very high (five shared components, not just topology). Net: the lean toward Option 2 strengthens, with Option 3 (no escape hatch) the live simplification to debate *within* it, and Option 4 receding on cost. Appendix F models the target API concretely.

The deciding question between them (new **Q7**): **do you want intent to live on the _type_ (Option 2) or on the _primitive_ (Option 4)?** Both satisfy "one type → one lane"; both kill #344 at the root. Option 2 keeps one unified pipeline and tags the type; Option 4 keeps the type clean and splits the subsystem. Either is a defensible, industry-aligned end-state — and both are strictly better than today's late-bound hybrid.

"Unopinionated, zero lock-in" should govern **provider** choice, not **messaging semantics**. Being opinionated that broadcast and work-queue are distinct is good opinion, not lock-in.

---

## 7. The central fork — **RESOLVED: Option 3** (owner's call, 2026-06-10 second session)

> **Decision: full type=intent, no escape hatch.** Every message is an event or a command; one type → one lane, enforced. Dual-lane needs are modeled as two types. #359 still ships as a transition-only correctness net (hard rule unchanged). The table below is preserved as the decision record.

| If you choose… | API direction | #359 becomes… | #344 narration |
|---|---|---|---|
| **Keep universal late-bound** | Option 1 | permanent universal isolation infra | "a bug we fixed well; tax paid forever" |
| **Type=intent + escape hatch** (lean) | Option 2 | the dual-lane substrate | "the isolation guarantee for opted-in dual-lane" |
| **Full type=intent** | Option 3 | a transition-only correctness net | "obsoleted by the model; collision now unrepresentable" |
| **Separate primitives** (Foundatio-style; close runner-up) | Option 4 | the *first step* of un-sharing the pipeline | "the topology half of a full subsystem split" |

**#359 ships regardless** — it is correct under all four branches. This fork changes only what #359 *means* and what the public API evolves into. Do **not** block the #359 plan on it.

---

## 8. Open questions for the next session

- **Q1 — How is type-level intent declared (Option 2/3)?** Marker interfaces (`IEvent`/`ICommand`, compile-enforced but pollutes POCOs) vs naming convention (`*Event`/`*Command`, clean but stringly) vs attribute (`[Event]`/`[Command]`) vs builder declaration (`ForMessage<T>().AsEvent()`, keeps POCOs pristine, intent in composition root). NServiceBus supports a configurable `DefiningEventsAs` convention — worth mirroring so users aren't forced into markers. *Evidence (§3.2 E2): markers-with-constraints and unobtrusive conventions can't share one overload — mixed mode forces unconstrained methods.* **DECIDED (2026-06-10, 2nd session): A1 — markers + generic constraints; conventions deliberately NOT mirrored** (they would forfeit compile-time enforcement). Full rationale and consequences in F.3-a.
- **Q2 — Collapse the four publish interfaces?** Can `IBus`/`IQueue`/`IOutboxBus`/`IOutboxQueue` become one `IMessageBus` with `Publish`/`Send` (type-enforced) where outbox-vs-direct is an ambient-transaction policy? What breaks in the outbox's transactional-visibility contract? *Evidence (§3.2 E1): the ambient mechanism already exists (`IOutboxTransactionAccessor` + writer branch); remaining design work is the no-ambient-tx behavior (direct vs durable store-first — today's outbox-without-tx does the latter) and the `Delay`-requires-storage quirk. Industry is unanimous on ambient policy.*
- **Q3 — Discovery + intent.** If intent moves to the type, `ForMessagesFromAssembly` can finally infer intent. Does discovery become the recommended default, with `ForMessage<T>` as the override/power surface?
- **Q4 — `_ValidateIntentHeader` fail-closed.** Independent of the fork: after #359 a mismatch means real breakage (topology drift, external/legacy publisher, version skew). Make it configurable nack/dead-letter (log in dev, reject in prod)? File as its own follow-up issue.
- **Q5 — Migration shape.** Greenfield (no deployed consumers), so breaking changes are on the table — but what is the cleanest path from `OnBus`/`OnQueue` to the chosen end-state for the framework's own packages and docs?
- **Q6 — Does the dual-lane escape hatch (Option 2) earn its keep,** or is a genuinely-both-lanes message always a modeling smell that Options 3/4 are right to forbid? *Evidence (§3.2 E5): one unit test, zero internal usage — no demonstrated demand inside the framework itself.*
- **Q7 — Intent on the _type_ (Option 2) or the _primitive_ (Option 4)?** The deciding question between the two strongest options. Option 2 keeps one unified pipeline and tags the type (event/command); Option 4 keeps the type clean and splits the subsystem (`IBus`/`IQueue` all the way down, Foundatio-style — native to Headless's building-blocks identity). Both satisfy "one type → one lane" and kill #344 at the root. Weigh: marker/convention tolerance (Option 2 needs one) vs internal-refactor appetite (Option 4 un-shares the pipeline #359 only topology-splits).

---

## 9. Next steps

1. ~~Owner resolves §7~~ **DONE — Option 3** (2026-06-10, second session).
2. Run `/x-brainstorm` to produce the requirements doc for Option 3, answering Q1 (declaration mechanism), Q2 + F.3 a–e (publish-surface collapse details), Q3 (discovery default), Q5 (path from `OnBus`/`OnQueue`). Appendix C Option 3 + Appendix F (minus `AsDualLane`) are the starting material. Verify no internal package relies on dual-lane first (§3.2 E5 says none does — re-confirm at execution time).
3. File Q4 (`_ValidateIntentHeader` fail-closed) as a standalone issue now — still useful as the transition-period net.
4. Keep the #359 plan executing in parallel; narrate it as "transition-only correctness net — obsoleted once the model makes the collision unrepresentable" (per the §7 table, Option 3 row).
5. Consider the adjacent improvements in Appendix B (declare-and-deploy topology, capability honesty) — orthogonal, still valuable.

---

## Appendix A — Code map & anchors

> Verified against `main` on 2026-06-10. **Re-confirm before acting** (see §0.1). Paths are repo-relative.

### A.1 Public API surface (the contract)
| Concern | Type / file |
|---|---|
| Registration entry | `ForMessage<T>(…)` — `src/Headless.Messaging.Core/Setup.cs:39` (setup-builder) and `:141` (IServiceCollection twin) |
| Message builder | `IMessageBuilder<T>` — `src/Headless.Messaging.Core/Registration/MessageBuilder.cs:14` (`MessageName`, `CorrelationFrom`, `OnBus<C>`, `OnQueue<C>`) |
| Consumer builders | `IConsumerBuilderBase` / `IBusConsumerBuilder` / `IQueueConsumerBuilder` — `src/Headless.Messaging.Core/Registration/ConsumerBuilders.cs:19,39,45` (`Group`, `Concurrency`, `HandlerId`, `WithCircuitBreaker`) |
| Assembly scan | `ForMessagesFromAssembly[Containing]` — `src/Headless.Messaging.Core/Setup.cs:65,83,102,116` (defaults scanned consumers to **Bus**) |
| Consume contract | `IConsume<T>` — `src/Headless.Messaging.Abstractions/IConsume.cs`; `ConsumeContext<T>` — `…/ConsumeContext.cs` (`IntentType` at `:314`) |
| Intent enum | `IntentType` — `src/Headless.Messaging.Abstractions/IntentType.cs` (`Bus`, `Queue`) |
| Publish — broadcast direct | `IBus.PublishAsync<T>` — `src/Headless.Messaging.Bus.Abstractions/IBus.cs:51` |
| Publish — broadcast outbox | `IOutboxBus.PublishAsync<T>` — `src/Headless.Messaging.Bus.Abstractions/IOutboxBus.cs:51` |
| Publish — p2p direct | `IQueue.EnqueueAsync<T>` — `src/Headless.Messaging.Queue.Abstractions/IQueue.cs:50` |
| Publish — p2p outbox | `IOutboxQueue.EnqueueAsync<T>` — `src/Headless.Messaging.Queue.Abstractions/IOutboxQueue.cs:50` |
| Transport interfaces | `IBusTransport` / `IQueueTransport` — `src/Headless.Messaging.{Bus,Queue}.Abstractions/I{Bus,Queue}Transport.cs` |

### A.2 The #344 seam (where the leak lives and is half-mitigated)
| Behavior | Anchor |
|---|---|
| Intent header constant | `Headers.Intent = "headless-intent"` — `src/Headless.Messaging.Abstractions/Headers.cs:123` |
| Stamp intent on publish | `IMessagePublishRequestFactory.cs:87` (`headers[Headers.Intent] = intentType.ToString()`) |
| Consume-side check (**log-only** — Q4 target) | `ConsumeMiddlewarePipeline._ValidateIntentHeader` — `…/ConsumeMiddlewarePipeline.cs:438` (called at `:56`) |
| Sender lane routing | `MessageSender._ResolveTransportAsync` — `…/IMessageSender.cs:152`; missing-transport runtime failure `_MissingTransportAsync` `:173` |

### A.3 The Kafka / missing-bus guard (already exists — see #359 U5)
| Behavior | Anchor |
|---|---|
| Startup intent-transport check | `IBootstrapper.Default.cs:385` (`_CheckIntentTransportSupport`, called `:347`) |
| Generic missing-bus throw | `_RequireTransportFor<TTransport>` — `IBootstrapper.Default.cs:416` (bus path `:401`, queue path `:408`) |
| Provider marker (for Kafka hint) | `MessageQueueMarkerService("Kafka"|"RabbitMQ"|…)` — registered in each provider's `Setup.cs` |

### A.4 Provider transports (current split status — see #359 plan for the full table)
- **Already split** (separate Bus/Queue classes): AWS (`AmazonSnsBusTransport`/`AmazonSqsQueueTransport`), Azure Service Bus, Redis (`RedisPubSubBusTransport`/`RedisTransport`), InMemory.
- **Still fused** (one class, both lanes → the leak): RabbitMQ (`RabbitMqTransport`), NATS (`NatsTransport`), Pulsar (`PulsarTransport`). These are #359's work.
- **Queue-only**: Kafka (`KafkaTransport : IQueueTransport`) — no `IBusTransport`; drives the guard.

---

## Appendix B — Adjacent design improvements (orthogonal to the fork)

These surfaced in the same discussion. They are **independent of the §7 model choice** and worth doing under any branch.

### B.1 Declare-and-deploy topology at startup (not lazy-create)
Today topology is lazily declared on first connect (e.g. `RabbitMqConsumerClient` declares exchange/queue in `ConnectAsync`). This carries MassTransit's well-known **publish-before-subscribe message-loss gotcha**: a message published before any subscriber has ever started is lost because no binding exists yet. A future design declares the full topology up front and **deploys it at bootstrap** (cf. MassTransit's `DeployPublishTopology`). Wins: no lost-before-subscribe, auditable topology (good for ops *and* AI reasoning), pairs naturally with the outbox. Note: the #359 conformance test partly exercises this; full deploy-at-startup is a separate improvement.

### B.2 Structural capability honesty (Kafka-as-Rider, not runtime guard)
Kafka-can't-bus is currently a runtime startup guard (#359 U5). The cleaner long-term shape makes the limitation **structural** — a bus-incapable transport simply doesn't expose a bus surface (cf. MassTransit's *Rider* concept, where Kafka is a separate rider, not a full bus transport). Then the misconfiguration is unrepresentable rather than caught by a string check. The U5 guard is the right *behavior* for now; this makes the bad state impossible later. Strongly synergistic with Option 4 (separate primitives).

### B.3 Collapse the four publish interfaces
`IBus`/`IQueue`/`IOutboxBus`/`IOutboxQueue` is a wide surface (W4). Under Options 2–4, this can collapse: outbox-vs-direct becomes an ambient-transaction policy rather than a separate interface (Wolverine ships one `IMessageBus`). Requires care around the outbox's transactional-visibility contract (Q2).

---

## Appendix C — Per-option work breakdown (for the executing agent)

> Only relevant once the owner picks in §7. Each assumes #359 has merged (or is merging).

### Option 1 — Unify late-bound intent (smallest)
1. Make `IBus.PublishAsync<T>` / `IQueue.EnqueueAsync<T>` validate that `T` has a matching-lane registration; throw/block on mismatch.
2. Promote `_ValidateIntentHeader` (Appendix A.2) from log-only to configurable fail-closed (= Q4).
3. Keep `ForMessage<T>` + `OnBus`/`OnQueue` + four publish interfaces as-is.
- *No model change; treats the symptom. Docs: clarify lane-must-match-registration.*

### Option 2 — Type=intent default + dual-lane escape hatch (recommended)
1. **Q1 decision first** — how intent is declared on the type (marker / convention / attribute / builder). Build that mechanism.
2. Add terse discovery (`AddConsumersFromAssemblyContaining<T>`) that infers intent from the type (fixes W3).
3. Collapse publish surface toward one `IMessageBus` with type-enforced `Publish`/`Send` (B.3); outbox as policy (Q2).
4. Add `ForMessage<T>().AsDualLane()` escape hatch — the **only** place #359's per-provider isolation + conformance applies.
5. Keep `ForMessage<T>` as the explicit override/power surface.
- *Docs: new default model + escape hatch; migration note for `OnBus`/`OnQueue` (Q5).*

### Option 3 — Full type=intent (biggest convergence)
- Option 2 minus the escape hatch. Every message is event or command; no dual-lane. #359 becomes a transition-only correctness net. Largest break; verify no internal package relies on dual-lane first.

### Option 4 — Separate primitives, Foundatio-style (close runner-up)
1. Split registration into `setup.Bus.ForMessage<T>(…)` and `setup.Queue.ForMessage<T>(…)` namespaces; drop the `OnBus`/`OnQueue` flag.
2. Un-share the pipeline: Bus and Queue get **separate** transport/storage/dispatch all the way down (this is the big internal refactor; #359 only topology-splits — this finishes the job).
3. Keep `IBus`/`IQueue` as the (already-separate) injection surfaces; key each by message type.
4. Pairs with B.2 (capability honesty) — a queue-only transport just has no `Bus` namespace.
- *Docs: position as the Foundatio-aligned building-block model. No type markers (cleaner "zero lock-in").*

---

## Appendix D — Decision ledger

| Item | State | Note |
|---|---|---|
| #344 resolution = physical separation | **DECIDED** (2026-05-26) | Do not reopen. |
| #359 plan written | **DONE** | `docs/plans/2026-06-10-001-…-plan.md`; ready to execute. |
| NATS = JetStream both lanes (not Core pub/sub) | **DECIDED** (this session) | Deviates from old spec §7; preserves durability (MassTransit rationale). |
| Pulsar = topic separation, keep `Shared` | **DECIDED** (this session) | Deviates from #359 R4's literal "Exclusive/Failover"; avoids intra-group scaling regression. |
| Redis = single package; Bus durability = docs-only | **DECIDED** (this session) | No package split; no startup-fail. |
| Kafka guard = extend existing `_RequireTransportFor` | **DECIDED** (this session) | Add §8 hint only; generic guard already exists. |
| §7 central fork (long-term API model) | **DECIDED** (2026-06-10, 2nd session) | **Option 3 — full type=intent, no escape hatch.** Owner's call; evidence in §3.2. |
| Q7 — intent on type (Opt 2) vs primitive (Opt 4) | **RESOLVED** | Type. (Opt 4 receded on verified cost.) |
| Q6 — dual-lane escape hatch | **RESOLVED** | Dropped — zero internal demand; dual-lane = two types. |
| Q1 — type-intent declaration mechanism | **DECIDED** (2026-06-10, 2nd session) | **A1: markers + generic constraints** (compile-time enforcement); conventions/builder classification deleted; unowned types wrapped; both-markers = boot check. Superseded an interim no-markers lean — see F.3-a. |
| Q4 — `_ValidateIntentHeader` fail-closed | **OPEN / actionable now** | File as standalone issue; useful under every branch. |
| Migration / upgrade paths | **OUT OF SCOPE** | Greenfield — no deployed consumers. |
| Appendix A anchors re-verified | **DONE** (2026-06-10, 2nd session) | All correct; mechanism evidence added (§3.2). |
| Outbox ambient mechanism exists | **FACT** (§3.2 E1) | `IOutboxTransactionAccessor` + `OutboxMessageWriter` branch; 4 interfaces are facades. |
| Dual-lane same-type: zero internal usage | **FACT** (§3.2 E5) | One unit test only; feeds Q6. |
| Option 4 un-sharing cost = very high | **FACT** (§3.2 E5) | 5 shared components beyond topology. |
| Target API modeled (Option 2 + Opt 4 delta) | **DONE** (2026-06-10, 2nd session) | Appendix F; sub-decisions F.3 a–e open. |

---

## Appendix E — Glossary

- **Bus lane / intent** — broadcast (publish/subscribe); every subscriber group gets a copy. API: `IBus.PublishAsync`, `OnBus<C>`.
- **Queue lane / intent** — point-to-point (competing consumers); one worker processes each message. API: `IQueue.EnqueueAsync`, `OnQueue<C>`.
- **`IntentType`** — the enum (`Bus`/`Queue`) that tags a registration and is stamped on the wire (`headless-intent` header).
- **Late-bound intent** — Headless's current model: the lane is chosen per-registration (`OnBus`/`OnQueue`) and per-publish-call, independent of message type. The same type can use both lanes — the root of #344.
- **Type=intent** — the MassTransit/NServiceBus model: the message *type* determines the lane (event→bus, command→queue); one type, one lane, enforced.
- **Separate primitives** — the Foundatio model: bus and queue are different abstractions/subsystems; you pick by which you inject.
- **Cross-intent leakage (#344)** — a message published on one lane reaching a consumer registered on the other lane for the same name, because both share broker topology.
- **Outbox** — transactional-publish path (`IOutboxBus`/`IOutboxQueue`) that persists the message in the same transaction as business state, then dispatches via a background processor.
- **Conformance test** — the cross-provider test (#359 U1) asserting lane isolation holds identically on every provider.
- **Dual-lane (escape hatch)** — a message deliberately opted into both lanes (Option 2's `AsDualLane()`); the only place isolation machinery is needed under that option.

---

## Appendix F — Modeled target API (what "well-designed" concretely looks like)

> Added 2026-06-10 (second session) per §0.1 job 1: prototype the API shapes so the §7/Q7 fork is a choice between artifacts, not abstractions. **F.1 is maintained in place at the decided state**: Option 3 (no `AsDualLane()`; a both-lanes need = two types) and **F.3-a decided after a DX deep-dive: A1 — marker interfaces + generic constraints** (an interim no-markers lean was considered and superseded; rationale in F.3-a). Earlier shapes are in git history. §F.4's Option 4 delta is preserved for the record.

### F.1 Full lifecycle (decided state: Option 3 + A1 markers/constraints)

```csharp
// ── Markers: the classification IS the type system (F.3-a DECIDED: A1) ──
// Ship in a dependency-free contracts package — three empty interfaces, nothing else.
public interface IMessage;                  // common base — lets the consume side constrain too
public interface IEvent : IMessage;         // broadcast lane
public interface ICommand : IMessage;       // point-to-point lane

public record OrderPlaced(string OrderId, decimal Total) : IEvent;
public record ProcessPayment(string OrderId) : ICommand;

// Unowned/3rd-party payloads: no override path exists — WRAP them (anti-corruption doctrine):
public record PaymentSettled(ExternalPayload Payload) : IEvent;

// ── Handlers: contract unchanged; constrained so unmarked types can't even be consumed ──
public interface IConsume<T> where T : IMessage { … }
public sealed class AuditHandler(IAuditStore store) : IConsume<OrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken ct) => …;
}

// ── Registration: discovery default; ForMessage<T> power surface. NO conventions, NO AsEvent() —
//    both are structurally dead under constraints (a convention-classified POCO could never satisfy them).
services.AddHeadlessMessaging(setup =>
{
    setup.UseSqlServer("conn");
    setup.UseRabbitMq(r => r.HostName = "localhost");

    setup.AddConsumersFromAssemblyContaining<OrderPlaced>();   // lane read from the marker — fixes W3

    setup.ForMessage<OrderPlaced>(m => m                        // power surface: name, correlation, consumer knobs
        .Name("orders.placed")
        .CorrelationFrom(o => o.OrderId)
        .Consumer<AuditHandler>(c => c.Group("audit").Concurrency(4)));  // no lane anywhere — the marker carries it
});

// ── Publish: ONE injected surface; constraints enforce the verb at COMPILE time ──
public interface IMessagePublisher
{
    Task PublishAsync<T>(T message, PublishOptions? options = null, CancellationToken ct = default)
        where T : IEvent;
    Task SendAsync<T>(T message, SendOptions? options = null, CancellationToken ct = default)
        where T : ICommand;
}

await publisher.PublishAsync(new OrderPlaced(…));     // OK
await publisher.PublishAsync(new ProcessPayment(…));  // COMPILE ERROR — ICommand doesn't satisfy where T : IEvent
await publisher.SendAsync(new OrderPlaced(…));        // COMPILE ERROR — symmetric
await publisher.PublishAsync(new Unmarked(…));        // COMPILE ERROR — no marker; mark it or wrap it

// ── The ONE hole constraints can't close: a type marked with both ──
public record Confused(…) : IEvent, ICommand;          // satisfies both constraints → compiles
// → BOOT ERROR (F.3-f): ambiguous classification, named explicitly.
//   Also reachable via inheritance (base brings IEvent, derived adds ICommand) — same boot check.

// ── Outbox: ambient policy, same verb (industry-converged; mechanism exists today) ──
// inside a transaction (IOutboxTransactionAccessor.Current set, e.g. via EF integration):
await publisher.PublishAsync(new OrderPlaced(…));     // → outbox row in same DbTransaction, post-commit dispatch
// outside a transaction:
await publisher.PublishAsync(new OrderPlaced(…));     // → direct transport (or durable store-first — F.3-d)

// ── No escape hatch (Option 3): a both-lanes need is modeled as two types ──
public record InventoryChanged(…) : IEvent;
public record ReprocessInventory(…) : ICommand;
```

### F.2 What collapses, what stays

| Today | Modeled |
|---|---|
| 4 publish interfaces (`IBus`/`IQueue`/`IOutboxBus`/`IOutboxQueue`) | 1 (`IMessagePublisher`); outbox = ambient policy over the existing `IOutboxTransactionAccessor` branch |
| `PublishOptions` / `EnqueueOptions` — isomorphic (§3.2 E1) | `PublishOptions` / `SendOptions` (or one `MessageOptions`); `Delay` consistent everywhere (F.3-e) |
| `OnBus<C>()` / `OnQueue<C>()` on every registration | `Consumer<C>()` — intent from type; `OnBus`/`OnQueue` survive only inside `AsDualLane()` |
| Scan hardcodes Bus (W3) | Scan infers intent from type |
| `ForMessage<T>` mandatory for the 95% case (W2) | Discovery default; `ForMessage<T>` = override/power surface (matches §3.2 E3 convergence) |
| `IntentType` on `ConsumeContext` as the only lane signal | Stays, now derivable from type except under `AsDualLane()` |
| Kafka = runtime startup guard (#359 U5) | Guard stays for lane availability; long-term structural honesty per B.2 (MassTransit Rider precedent, §3.2 E4) |

What this kills, by weakness: W1 (intent reconciled by construction), W2 (discovery + type = near-zero config), W3 (scan infers), W4 (one surface), W5 (one decision point — the type — instead of two per call site).

### F.3 Sub-decisions inside this model (feeds Q1/Q2)

- **(a) Enforcement level — DECIDED (2026-06-10, owner, after DX deep-dive): A1 — markers + generic constraints.** `where T : IEvent` / `where T : ICommand` make wrong-verb and unmarked-type compile errors at every call site with zero machinery; `IConsume<T> where T : IMessage` closes the consume side too. Accepted consequences: (i) conventions (`DefiningEventsAs`) and builder classification (`.AsEvent()`) are **deleted from the design** — convention-classified POCOs could never satisfy the constraints, so the whole predicate surface disappears (simpler API); (ii) unowned/3rd-party types are **wrapped in owned records** (anti-corruption doctrine: your messages are your contracts); (iii) the residual hole is both-markers → boot check (F.3-f). Markers ship in a dependency-free contracts package. Decision history: an interim no-markers lean was based on "both verbs compile anyway" — true only when conventions coexist with markers (mixed mode forces unconstrained methods); markers-only restores genuine compile-time enforcement, which dominated the DX comparison (feedback latency, truth locality, refactor safety, AI-legibility, test ergonomics) against B's purity win.
- **(b) Naming.** New single noun (`IMessagePublisher`) vs keeping `IBus` as the one surface. `IBus` collides with today's broadcast-only meaning; a rename marks the semantic break.
- **(c) Options types.** Unify `PublishOptions`/`SendOptions` into `MessageOptions`+`Delay`, or keep two isomorphic records for future divergence.
- **(d) No-ambient-tx behavior.** Direct transport (Wolverine/MT precedent) vs durable store-first (today's `IOutbox*`-without-tx behavior). A `DeliveryMode` option/policy could expose both; the default matters most.
- **(e) `Delay` on direct.** Auto-route delayed messages via storage (delay implies durability) vs throw. Silent-ignore (today's behavior) must die either way.
- **(f) Ambiguous classification — a type that is *both*** *(simplified by the A1 decision)*. C# cannot forbid `record Foo : IEvent, ICommand` (also reachable via inheritance: base brings `IEvent`, derived adds `ICommand`), and constraints don't catch it — a both-type satisfies `where T : IEvent` *and* `where T : ICommand`, so both verbs compile. A both-type is the dual-lane escape hatch re-entering through the type system — under Option 3 it is a **hard error, never a precedence rule**. With conventions deleted (F.3-a), the ladder collapses to: *exactly one marker, or error* — no predicate-ambiguity case exists. Enforcement: **boot check** at registration drain / bootstrap (the `Bootstrapper` intent-transport validation is the natural home) naming the type and both marker sources, plus a first-publish backstop for types never seen by registration. Optional later: a Roslyn analyzer flagging both-markers at compile time. Anti-precedent: NServiceBus's independent `IsCommand`/`IsEvent` predicates fail lazily per-operation (`Publish` → "is a command", `Send` → "is an event") — diagnose at boot instead.

### F.4 The Option 4 delta (for contrast)

```csharp
setup.Bus.ForMessage<OrderPlaced>(m => m.Name("orders.placed").Consumer<AuditHandler>());
setup.Queue.ForMessage<FulfillOrder>(m => m.Consumer<FulfillWorker>(c => c.Concurrency(8)));

await bus.PublishAsync(new OrderPlaced(…));      // IBus — broadcast subsystem
await queue.EnqueueAsync(new FulfillOrder(…));   // IQueue — separate subsystem end-to-end
```

- No markers, conventions, or runtime intent checks needed — the primitive *is* the intent. POCOs stay pristine with no Q1 at all.
- Outbox collapse applies per primitive: `IOutboxBus`→folds into `IBus`, `IOutboxQueue`→`IQueue` (2 surfaces, not 1).
- Kafka honesty is free: a queue-only provider simply registers nothing under `setup.Bus` (B.2 structurally, the Rider shape).
- **The price (verified, §3.2 E5):** un-sharing storage schema, `Dispatcher`, `ConsumeMiddlewarePipeline`, registration model, and publish pipeline. #359 pays only the topology slice of this.
- Dual-lane = define two types; no escape hatch exists or is needed.
