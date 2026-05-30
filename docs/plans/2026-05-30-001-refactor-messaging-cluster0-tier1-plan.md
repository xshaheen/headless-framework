---
title: "refactor: Messaging Cluster 0 Tier 1 — relocate PublishOptions + Topic→MessageName rename"
status: completed
type: refactor
created: 2026-05-30
depth: standard
issues: [355, 356]
closes: [345]
origin: docs/brainstorms/2026-05-25-messaging-consumer-model-evolution-requirements.md
roadmap: 217
---

# refactor: Messaging Cluster 0 Tier 1 — relocate `PublishOptions` + `Topic → MessageName` rename

Tier 1 of the Cluster 0 consumer-model evolution. Two parallel-but-file-overlapping issues, sequenced here as **#355 → #356** so the larger rename runs against the final package layout. Cluster 0 blocks every other messaging cluster (#217), so these land first.

**Origin spec:** `docs/brainstorms/2026-05-25-messaging-consumer-model-evolution-requirements.md` (§3, §4, §10, §11). **Roadmap:** #217.

---

## Summary

Two mechanical-but-breaking refactors in the messaging abstraction layer:

1. **#355 — relocate `PublishOptions`** from `Headless.Messaging.Abstractions` → `Headless.Messaging.Bus.Abstractions`, mirroring `EnqueueOptions` in `Queue.Abstractions`. Completes the package-layout split (#345). Source verification shrank this to a **single-type move** — the shared data types the issue/spec list (`TransportMessage`, `OperateResult`, `MessageHeader`, `BrokerAddress`) are *already* in `Abstractions` (PR #340).

2. **#356 — rename `Topic → MessageName`** across the framework's universal layer. "Topic" is broker-leaky; `MessageName` reads correctly for both intent lanes (bus + queue). This is a **full vocabulary rename** (the entire `MessagingConventions` `Topic*` surface, not just one method), with provider-native `Topic` terminology (Kafka `TopicOptions`, ASB `TopicName`, SNS `Topic`) deliberately left intact.

Greenfield project, no deployed consumers — clean breaking change, no type-forwarding or compatibility shims (origin §11 "No type-forwarding").

---

## Problem Frame

The Bus/Queue abstraction split (PR #340) left `PublishOptions` stranded in shared `Abstractions` while its symmetric twin `EnqueueOptions` already lives in `Queue.Abstractions`. And the pre-split vocabulary still calls the universal logical-message-identity concept `Topic` — a word that fits Kafka/ASB/SNS but is wrong for RabbitMQ exchanges and nonsensical for queue (point-to-point) intent. Both are breaking-surface changes that must land before any consumer-facing API (`ForMessage<T>`, Cluster 0.3+) is built on top, otherwise downstream PRs chase a moving abstraction.

### Source facts established by verification (corrects the issue text)

| Claim in issue/spec | Actual source state | Consequence |
|---|---|---|
| #355: `TransportMessage`/`OperateResult` "already in Abstractions" | ✅ Confirmed — also `MessageHeader`, `BrokerAddress`. PR #340 moved them. | #355 = `PublishOptions` only |
| Spec §11: extract shared `ITransport` into Abstractions, rebase markers onto it | `IBusTransport`/`IQueueTransport` are **self-contained** (don't extend `ITransport`); `ITransport` is Core-internal only | **Deferred** — not load-bearing (see Scope Boundaries) |
| #355: "8 provider `using` directive updates" | `PublishOptions` namespace is `Headless.Messaging` (root), unchanged by the move | **No `using` changes** — only project-reference graph matters |
| #356: rename `PublishOptions.Topic` and `EnqueueOptions.Topic` (two properties) | `Topic` lives **once** on shared base `MessagePublishOptionsBase.Topic`, inherited by both | One property rename, not two |
| #356: rename `GetTopicForType<T>()` | No such method. Real surface is `GetTopicName(Type)` **plus** a whole `Topic*` config vocabulary | Full vocabulary rename (see U2) |

---

## Key Technical Decisions

### D1 — #355 scope: `PublishOptions` only; `ITransport` extraction held as a separate Tier-1 unit (U4)

Keep the **#355 PR** to the single `PublishOptions` move per the issue's explicit narrowing — a clean, single-concern relocation.

**Correction (post-review):** my original deferral rationale claimed the `ITransport` extraction "would touch all 8 providers' transport classes." That is **wrong** — interface inheritance is transparent to implementers. The real cost is ~4 files: move `ITransport.cs` Core→`Abstractions` (keep namespace `Headless.Messaging.Transport`; only `Core/Configuration/IMessagesOptionsExtension.cs` and `InMemory` reference it directly), make `IBusTransport`/`IQueueTransport` extend it (dropping their now-duplicate `BrokerAddress`/`SendAsync` declarations), and rewrite `MessageSender._ResolveTransportAsync` to return `ITransport` directly — which **deletes the `DispatchTransport` duck-typing struct** (`src/Headless.Messaging.Core/Internal/IMessageSender.cs:483`) that exists solely because the two markers share no base. The ~14 marker implementers need no edits.

So it is cheap *and* it removes a workaround — exactly the kind of compat hack greenfield should not keep. It is also legitimately part of #345 (spec §11), which #355 closes.

*Decision:* do it in Tier 1, but as a **distinct unit (U4) / separate PR**, not bundled into the #355 PR — preserving #355's single-concern hygiene (the user's stated reason for choosing "PublishOptions only"). If you'd rather drop U4 entirely and file it as a future issue, say so and I'll cut it.

### D2 — #356: full `Topic* → MessageName*` vocabulary rename

Rename the entire universal `Topic` surface, not just the properties the issue lists. Half-renaming would leave `ConsumeContext.MessageName` resolved by `MessagingConventions.GetTopicName()` / `TopicNaming` — an incoherent public surface. This is the greenfield breaking-change window (the reason Cluster 0 goes first); do the rename once, completely.

Proposed member renames (directional — implementer confirms final names at execution):

| Before | After |
|---|---|
| `ConsumeContext.Topic` | `MessageName` |
| `MessagePublishOptionsBase.Topic` | `MessageName` |
| `MessagingConventions.TopicNaming` | `MessageNaming` |
| `MessagingConventions.TopicPrefix` | `MessageNamePrefix` |
| `MessagingConventions.TopicSuffix` | `MessageNameSuffix` |
| `MessagingConventions.GetTopicName(Type)` | `GetMessageName(Type)` |
| `MessagingConventions.UseKebabCaseTopics()` | `UseKebabCaseMessageNames()` |
| `MessagingConventions.UseTypeNameTopics()` | `UseTypeNameMessageNames()` |
| `MessagingConventions.WithTopicPrefix(...)` | `WithMessageNamePrefix(...)` |
| `MessagingConventions.WithTopicSuffix(...)` | `WithMessageNameSuffix(...)` |
| `TopicNamingConvention` enum (+ members) | `MessageNamingConvention` |
| `MessagingConventionsExtensions.*Topic*` mirrors | mirror to `*MessageName*` |
| `RuntimeSubscriptionOptions.Topic` | `MessageName` |
| `RuntimeSubscriptionHandle.Topic` (+ ctor `topic` params) | `MessageName` |
| `IConsumerBuilder<T>.Topic(string)` | `MessageName(string)` |
| `IMessagingBuilder.WithTopicMapping<T>(string)` | `WithMessageNameMapping<T>(string)` |
| `MessagingOptions.TopicMappings` (internal) | `MessageNameMappings` |
| `MessagingOptions.TopicNamePrefix` | `MessageNamePrefix` |
| `MessagingOptions.WithTopicMapping(Type, string)` (internal) | `WithMessageNameMapping(...)` |
| `MessagingSetupBuilder.WithTopicMapping<T>` | `WithMessageNameMapping<T>` |
| `PreparedPublishMessage.Topic` (internal, Core) | `MessageName` |

> **Review correction:** the universal `Topic` surface is larger than `MessagingConventions` alone. The runtime-subscriber API (`IRuntimeSubscriber.cs`), the registration builders (`IConsumerBuilder`, `IMessagingBuilder`), and `MessagingOptions` each carry their own public/visible `Topic*` vocabulary. All are in scope for #356 — leaving them would force developers to configure `.Topic(...)` / `WithTopicMapping(...)` while consuming `ConsumeContext.MessageName`.

### D3 — Provider-native `Topic` stays (the exclude rule)

The rename touches **only the universal/framework layer**. Provider-native `Topic` terminology is correct domain vocabulary and must NOT be renamed:

- **Kafka:** `KafkaTopicOptions`, `TopicOptions`, `CreateTopicsAsync`, `TopicSpecification`, `TopicPartitionOffset`, `AllowAutoCreateTopics`, `FetchTopicsAsync`, log event `KafkaTopicMessagePublished`
- **Azure Service Bus:** `TopicName` (native entity)
- **AWS:** SNS `Topic` (native primitive)

The signal for "rename me": the symbol resolves to `ConsumeContext.Topic`, `MessagePublishOptionsBase.Topic`, or a `MessagingConventions` member. Everything else is provider-native. Execution should be compiler-driven (rename declarations, then fix build errors), with manual triage on each provider hit against this rule — **not** a blind find-replace.

### D4 — Sequence #355 → #356

Both issues are independent but both touch `PublishOptions.cs`. Landing #355 first means the larger #356 rename runs against the final file location (`Bus.Abstractions/PublishOptions.cs`), avoiding interleaved edits on the same file (origin #356 "Coordination" note). Reverse order also works; this plan picks #355-first.

---

## Scope Boundaries

**In scope:** the `PublishOptions` relocation (#355), the universal `Topic → MessageName` rename (#356) across abstractions + Core + providers + tests, and the corresponding doc sync.

### Deferred to Follow-Up Work

- **`MessageName` inference/collision/validation behavior** (origin §10: namespace-agnostic collision fail-fast, naming-policy validation, length limits). That is `ForMessage<T>` territory — **Cluster 0.3 (#357)**, not Tier 1. This plan renames the *existing* `GetTopicName` convention surface; it does not add new inference or validation semantics.

> Note: `ITransport` extraction (spec §11) was originally deferred here on a faulty cost estimate; it is now **U4** in this plan (see D1). It can be re-deferred to a future issue on request.

### Out of scope (other clusters / not this product surface)

- `ForMessage<T>` registration API, Layer 2 knobs, dual-lane topology, dispatch-side intent absorption — Cluster 0.3–0.6 (#357–#360).
- Anything in Clusters 1–20.

---

## System-Wide Impact

| Surface | Effect |
|---|---|
| **Public NuGet contract** | Breaking: `PublishOptions` moves package (same namespace, but a consumer referencing only `Messaging.Abstractions` for it now needs `Bus.Abstractions`); `Topic` members renamed across `Abstractions` + `Queue.Abstractions`. Greenfield → acceptable. |
| **Provider packages (8)** | #355: no change (already reference `Bus.Abstractions` where needed). #356: each provider that reads `context.Topic` / `options.Topic` / `GetTopicName` updates call sites; provider-native `Topic` untouched. |
| **Tests** | ~9 test projects reference `PublishOptions`; many reference the renamed `Topic` symbols. All must compile + pass against the new names. |
| **Docs** | `docs/llms/messaging.md` + package READMEs use `MessageName` for the universal concept (provider READMEs keep provider-native `Topic`). Doc-sync trigger per `CLAUDE.md` (public API surface change). |

---

## Implementation Units

### U1. Relocate `PublishOptions` to `Bus.Abstractions` (#355, closes #345)

**Goal:** Move the `PublishOptions` record into `Headless.Messaging.Bus.Abstractions`, symmetric with `EnqueueOptions` in `Queue.Abstractions`. No behavior change.

**Requirements:** #355; origin §11 "Layout fix (#345)". Partially closes #345 (the `PublishOptions` placement); **U4** completes #345 (the `ITransport` base). Close #345 manually only after both land.

**Dependencies:** none.

**Files:**
- Move `src/Headless.Messaging.Abstractions/PublishOptions.cs` → `src/Headless.Messaging.Bus.Abstractions/PublishOptions.cs` (preserve `namespace Headless.Messaging`, the header comment, `[PublicAPI]`, and the custom `Equals`/`GetHashCode`).
- Verify (do not assume) each project that references the **concrete** `PublishOptions` type already has a `ProjectReference` to `Headless.Messaging.Bus.Abstractions`: confirmed for `Headless.Messaging.Core`; add the reference to any test project that fails to compile (e.g. `tests/Headless.Messaging.Core.Tests.Unit`, `tests/Headless.Messaging.Abstractions.Tests.Unit`, `tests/Headless.Messaging.Testing.Tests.Unit`, and the `*.Integration` projects in the consumer list).
- `MessagePublishOptionsBase` **stays** in `Abstractions` (shared base — do not move).

**Approach:**
- Pure file relocation. The namespace is unchanged (`Headless.Messaging`), so **no `using` directive edits** are needed anywhere — the issue's "8 provider using updates" is inaccurate.
- The only real failure mode is a project that uses `PublishOptions` without a `Bus.Abstractions` reference. Source confirms `Abstractions` and `Queue.Abstractions` "references" are all `MessagePublishOptionsBase` crefs (false positives), so no layering violation is exposed by the move.
- No type-forwarding attribute (origin §11; greenfield).

**Patterns to follow:** `src/Headless.Messaging.Queue.Abstractions/EnqueueOptions.cs` (the symmetric placement and record shape this mirrors).

**Test suite design:** No new tests. Existing equality/behavior tests for `PublishOptions` (in `tests/Headless.Messaging.Abstractions.Tests.Unit` / `Core.Tests.Unit`) move with their type's package reference and must keep passing unchanged. This unit is non-feature-bearing (relocation only).

**Test scenarios:** `Test expectation: none — relocation with no behavior change.` Existing `PublishOptions` equality/`with`-expression tests must continue to pass after the project-reference fix-ups; no new assertions.

**Verification:** `make build` clean; `make test` green (specifically the messaging unit + abstractions test projects). `grep -rn "class PublishOptions\|record PublishOptions" src` resolves only to `Bus.Abstractions`. No new compiler warnings (SDKs treat warnings as errors in CI).

---

### U2. Rename `Topic → MessageName` across the universal layer (#356)

**Goal:** Rename the framework's universal logical-message-identity vocabulary from `Topic*` to `MessageName*`, leaving provider-native `Topic` terminology intact. Atomic — declarations + every call site + tests land together so the build stays green.

**Requirements:** #356; origin §4 (intent model / terminology), §10 (message-name inference surface), §1 "Terminology". Paves the way for #341/#357 (`ForMessage<T>` uses `x.MessageName(...)`).

**Dependencies:** U1 (so the rename touches the relocated `Bus.Abstractions/PublishOptions.cs`).

**Files (declaration surface — source of truth):**
- `src/Headless.Messaging.Abstractions/ConsumeContext.cs` — `Topic` property (line ~237) + 2 XML-doc prose refs (lines ~231, ~234).
- `src/Headless.Messaging.Abstractions/MessagePublishOptionsBase.cs` — `Topic` property + its `Equals`/`GetHashCode` usage + the class-summary prose ("topic, identifiers…").
- `src/Headless.Messaging.Abstractions/MessagingConventions.cs` — `TopicNaming`, `TopicPrefix`, `TopicSuffix`, `GetTopicName`, `UseKebabCaseTopics`, `UseTypeNameTopics`, `WithTopicPrefix`, `WithTopicSuffix`, `TopicNamingConvention` enum (per D2 table).
- `src/Headless.Messaging.Abstractions/MessagingConventionsExtensions.cs` — `UseKebabCaseTopics`, `WithTopicPrefix`, `WithTopicSuffix` mirrors (leave `WithDefaultGroup` — unrelated).
- `src/Headless.Messaging.Bus.Abstractions/PublishOptions.cs` — XML doc prose ("explicit topic…") if present after U1.
- `src/Headless.Messaging.Queue.Abstractions/EnqueueOptions.cs` — XML doc prose ("explicit topic…").
- `src/Headless.Messaging.Abstractions/IRuntimeSubscriber.cs` — `RuntimeSubscriptionOptions.Topic` (public), `RuntimeSubscriptionHandle.Topic` (public) + the `topic` constructor/factory params (lines ~46–127). **Public runtime-subscription surface — must rename.**
- `src/Headless.Messaging.Core/IConsumerBuilder.cs` — `Topic(string)` method (line ~55) + the `.Topic(...)` doc-snippet examples.
- `src/Headless.Messaging.Core/IMessagingBuilder.cs` — `WithTopicMapping<TMessage>(string)` (line ~188) + doc-snippet examples.
- `src/Headless.Messaging.Core/Configuration/MessagingOptions.cs` — `TopicMappings` (internal), `TopicNamePrefix` (public), `WithTopicMapping(Type, string)` (internal).
- `src/Headless.Messaging.Core/Configuration/MessagingSetupBuilder.cs` — `WithTopicMapping` implementations (lines ~124, ~148–153).
- `src/Headless.Messaging.Core/Internal/IMessagePublishRequestFactory.cs` — internal `PreparedPublishMessage.Topic` + exception-message text referring to "Topic".

**Files (call-site sweep — compiler-driven, apply D3 exclude rule):**
- `src/Headless.Messaging.Core/**` (~13 files in `Internal/`, `Configuration/`, root) — publishers, pipeline, conventions consumers reading `options.Topic` / `context.Topic` / `GetTopicName`; includes `Internal/IRuntimeConsumerRegistry.cs` and `Internal/RuntimeSubscriber.cs` (runtime-subscription `Topic` call sites), `Setup.cs` and `Configuration/MessagingSetupBuilder.cs` (`WithTopicMapping` doc snippets + calls).
- Provider packages where they read the **universal** symbol only: `RabbitMq`, `Aws`, `Kafka`, `AzureServiceBus`, `Pulsar`, `Nats`, `Redis`, `InMemory`, `Testing`. **Skip every provider-native `Topic`** per D3.
- Test projects asserting on the renamed symbols: `tests/Headless.Messaging.Core.Tests.Unit/**`, `Abstractions.Tests.Unit`, `Testing.Tests.Unit`, provider `*.Tests.Unit`, and the `*.Tests.Integration` projects (`Nats`, `NatsPostgreSql`, `Pulsar`, `Aws`).

**Approach:**
- Execution note below.
- Rename declarations first, build, then resolve each compiler error. For every provider hit, decide rename-vs-keep against D3 (universal → rename; native → keep). This is the riskiest step — a blind find-replace breaks Kafka/ASB/SNS.
- Watch substring traps: `Topic` is a substring of provider symbols (`TopicSpecification`, `KafkaTopicMessagePublished`). Use word-boundary / symbol-aware renames, not text replace.
- After the sweep, the only remaining `Topic` tokens in `src/Headless.Messaging.*` should be provider-native. Confirm with a final grep + manual scan.

**Execution note:** Compiler-driven rename — change the declarations, let the build surface every call site, triage each against D3. Do not attempt a repo-wide text substitution.

**Patterns to follow:** existing `MessagingConventions` fluent-method shape (keep the same return-`this` chaining and XML-doc style under the new names).

**Test suite design:** Unit coverage owns this. The behavior is unchanged — these are rename-rather-than-rewrite edits — so existing tests in `tests/Headless.Messaging.Core.Tests.Unit` and `tests/Headless.Messaging.Abstractions.Tests.Unit` are updated to the new names and must pass. Add focused assertions only where the rename touches the public convention surface (see scenarios). No new test infrastructure.

**Test scenarios:**
- Covers §10 inference. `MessagingConventions.GetMessageName(typeof(SomeMessage))` returns the same string the old `GetTopicName` produced for the same input under each `MessageNamingConvention` member (`TypeName`, `KebabCase`) — guards the rename didn't alter inference behavior.
- `WithMessageNamePrefix("x.")` + `WithMessageNameSuffix(".v1")` compose into the resolved name exactly as the old `WithTopicPrefix`/`WithTopicSuffix` did (prefix + base + suffix ordering preserved).
- `UseKebabCaseMessageNames()` / `UseTypeNameMessageNames()` flip `MessageNaming` to the expected enum member (both class method and extension-method variants).
- A consumed message exposes its name via `ConsumeContext.MessageName` (property carries the value previously read from `Topic`); round-trip publish→consume preserves it.
- `PublishOptions`/`EnqueueOptions` value-equality still holds when `MessageName` (formerly `Topic`) matches and differs when it differs — confirms the `Equals`/`GetHashCode` rename on the base record is wired correctly.
- Provider-native guard: a Kafka test referencing `KafkaTopicOptions` / `TopicSpecification` still compiles and passes unchanged — proves the exclude rule held.
- Registration surface: `IMessagingBuilder.WithMessageNameMapping<OrderPlaced>("orders.placed")` then resolving `OrderPlaced` yields `"orders.placed"` (same behavior the old `WithTopicMapping` had); `IConsumerBuilder.MessageName("...")` sets the consumer's name override. Guards the public builder rename didn't alter mapping behavior.
- Runtime subscription: subscribing via `IRuntimeSubscriber` with `RuntimeSubscriptionOptions { MessageName = "..." }` produces a `RuntimeSubscriptionHandle` whose `MessageName` carries that value — confirms the runtime-surface rename + call sites in `RuntimeSubscriber.cs` are wired.

**Verification:** `make build` clean (warnings-as-errors); `make test` green including `make test-integration` for the affected providers if Docker is available, else note it as untested locally. `grep -rnw "Topic" src/Headless.Messaging.Abstractions src/Headless.Messaging.Bus.Abstractions src/Headless.Messaging.Queue.Abstractions src/Headless.Messaging.Core` returns **zero** universal-layer hits (only provider-native tokens, if any, remain elsewhere). The two failing-then-passing inference scenarios above are implemented.

---

### U3. Doc sync — `MessageName` in agent docs + package READMEs (#356)

**Goal:** Bring the two agent-facing doc surfaces and package READMEs in line with the renamed public surface, per the `CLAUDE.md` doc-sync trigger (public API surface change).

**Requirements:** #356 acceptance ("README + `docs/llms/messaging.md` updated to use `MessageName`"); origin §1 terminology.

**Dependencies:** U2 (names must be final first).

**Files:**
- `docs/llms/messaging.md` — universal `Topic` prose → `MessageName`.
- `src/Headless.Messaging.Abstractions/README.md`, `src/Headless.Messaging.Bus.Abstractions/README.md`, `src/Headless.Messaging.Queue.Abstractions/README.md`, `src/Headless.Messaging.Core/README.md` — universal-concept references.
- Provider READMEs (`Kafka`, `Aws`, `AzureServiceBus`, `RabbitMq`, `Nats`, `Redis`, `Pulsar`, `InMemory`, `Testing`, `OpenTelemetry`) — **only** where they describe the universal `MessageName` concept; keep provider-native `Topic` prose (Kafka topics, ASB topics, SNS topics) as-is. Same D3 triage as code.

**Approach:**
- Read `docs/authoring/AUTHORING.md` first (per `CLAUDE.md`: the `docs/llms/<domain>.md` + package README pair must move in lockstep and follow its drift checks).
- Triage each `topic` mention: universal logical-name concept → `MessageName`; broker-native primitive → leave. Update any code snippets that call the renamed convention methods.
- Apply the `transport-wrapper-drift-and-doc-sync` learning (`docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md`) — keep README and llms doc consistent in the same change.

**Test suite design:** N/A — documentation.

**Test scenarios:** `Test expectation: none — documentation only.`

**Verification:** No universal-layer `Topic` prose remains in `docs/llms/messaging.md` or the abstraction-package READMEs (grep + manual read). Provider-native `Topic` prose preserved. AUTHORING.md drift checks pass. `make format-check` clean if it covers markdown; otherwise visual review.

---

### U4. Extract shared `ITransport` into `Abstractions`; remove `DispatchTransport` (completes #345)

> **Added after the Antigravity review corrected D1.** Originally deferred on a faulty "touches 8 providers" estimate. Real cost is ~4 files and it deletes a duck-typing workaround. Ships as its own PR (not bundled into #355). **Drop on request** if you'd rather file it as a future issue.

**Goal:** Give `IBusTransport`/`IQueueTransport` a shared `ITransport` base in `Abstractions` so `MessageSender` can consume one interface, eliminating the `DispatchTransport` struct. Completes the §11/#345 layout intent.

**Requirements:** origin §11 (shared `ITransport` base + marker subtypes); remainder of #345.

**Dependencies:** U1 (final `Bus.Abstractions` layout). Independent of U2/U3 — can land before or after the #356 PR.

**Files:**
- Move `src/Headless.Messaging.Core/Transport/ITransport.cs` → `src/Headless.Messaging.Abstractions/` (keep `namespace Headless.Messaging.Transport`; do not rename the type).
- `src/Headless.Messaging.Bus.Abstractions/IBusTransport.cs` — extend `ITransport`; remove the now-inherited `BrokerAddress`/`SendAsync` declarations (keep the capability XML docs).
- `src/Headless.Messaging.Queue.Abstractions/IQueueTransport.cs` — same.
- `src/Headless.Messaging.Core/Internal/IMessageSender.cs` — rewrite `_ResolveTransportAsync` / `_MissingTransportAsync` to return `ITransport?`; delete the `DispatchTransport` `record struct` and its `ForBus`/`ForQueue` factories; call `transport.SendAsync` / `transport.BrokerAddress` directly.
- `src/Headless.Messaging.Core/Configuration/IMessagesOptionsExtension.cs` — verify its `ITransport` reference still resolves (namespace unchanged; should be a no-op).
- `src/Headless.Messaging.InMemory/InMemoryBusTransport.cs`, `Setup.cs` — verify (only non-Core direct `ITransport` referencers); expect no edits.

**Approach:**
- Interface inheritance is transparent to the ~14 marker implementers — they already declare matching members, so adding the base touches none of them.
- The move keeps `ITransport`'s namespace, so Core call sites resolve unchanged once `Abstractions` declares it (`Bus`/`Queue.Abstractions` can now reference it without depending on `Core`).
- No type-forwarding (greenfield).

**Patterns to follow:** the existing `IBusTransport`/`IQueueTransport` XML-doc capability-declaration style; preserve it on the marker subtypes.

**Test suite design:** Unit. Behavior is unchanged (the publish path still calls `SendAsync` on the resolved transport) — existing `MessageSender`/dispatch tests in `tests/Headless.Messaging.Core.Tests.Unit` must pass unchanged. No new infrastructure.

**Test scenarios:**
- Bus publish resolves an `IBusTransport` and dispatches via `ITransport.SendAsync` — same `OperateResult` as before the refactor.
- Queue enqueue resolves an `IQueueTransport` and dispatches via `ITransport.SendAsync`.
- Missing-transport path (no registered transport for the intent) still returns the existing failure `OperateResult` (the `_MissingTransportAsync` behavior is preserved after dropping `DispatchTransport`).
- Type check: `typeof(ITransport).IsAssignableFrom(typeof(IBusTransport))` and `…(typeof(IQueueTransport))` — confirms the base relationship exists.

**Verification:** `make build` clean (warnings-as-errors); `make test` green. `grep -rn "DispatchTransport" src` returns nothing. `grep -rn "interface ITransport" src` resolves only to `Abstractions`. No provider transport class was edited (`git diff --stat` shows no provider `*Transport.cs` changes).

---

## Sequencing

```
U1 (#355 relocate)  ──►  U2 (#356 rename, atomic)  ──►  U3 (#356 docs)
   PR for #355              ├─────────── PR for #356 ───────────┤

U4 (ITransport extraction)  — depends only on U1; own PR; land any time after #355
```

- **U1** ships as the #355 PR (closes #345 *partially* on merge; #345 fully closed once U4 lands).
- **U2 + U3** ship together as the #356 PR (code rename + its doc sync in one reviewable unit).
- **U4** ships as its own PR after U1. Independent of #356; order-flexible.

---

## Risks & Mitigations

| Risk | Likelihood | Mitigation |
|---|---|---|
| Blind rename breaks provider-native `Topic` (Kafka/ASB/SNS) | Medium | D3 exclude rule + compiler-driven, symbol-aware rename (not text replace) + provider-native guard test (U2) |
| Missed call site → red build | Low | Atomic rename; build *is* the completeness check. CI warnings-as-errors catches stragglers |
| Test project missing `Bus.Abstractions` ref after U1 | Low | U1 explicitly verifies + adds refs per build errors |
| Integration tests need Docker, unavailable locally | Medium | Run `make test-unit` always; note integration coverage as locally-untested if Docker absent; CI runs it |
| Doc drift between README and `llms/messaging.md` | Low | U3 follows AUTHORING.md lockstep rule + the doc-sync learning |
| U4: removing duplicate members from markers changes binary/source compat | Low | Greenfield, no consumers; members are identical on the new base, so implementers are unaffected; build is the check |
| U4 scope-creep into #355 PR | Low | U4 is a separate PR/unit by design (D1); #355 PR stays PublishOptions-only |

---

## Open Questions

1. **Final member names for the `MessagingConventions` vocabulary** (D2 table) — `MessageNaming` / `GetMessageName` / `MessageNamingConvention` are proposed; confirm at execution (e.g., is `GetMessageName(Type)` preferred over keeping a `...ForType` suffix?). Low-stakes; implementer's call against surrounding naming.
2. **Does `MessageName` inference behavior change at all in Tier 1?** Plan says **no** — pure rename of the existing `GetTopicName` surface. New collision/validation semantics (§10) are Cluster 0.3 (#357). Flagged so the implementer doesn't pull §10 behavior forward.

---

## References

- Origin spec: `docs/brainstorms/2026-05-25-messaging-consumer-model-evolution-requirements.md` (§1, §3, §4, §10, §11)
- Roadmap: #217 · Issues: #355, #356 · Closes: #345
- Learning: `docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md`
- Authoring rules: `docs/authoring/AUTHORING.md`
- Symmetric pattern: `src/Headless.Messaging.Queue.Abstractions/EnqueueOptions.cs`
