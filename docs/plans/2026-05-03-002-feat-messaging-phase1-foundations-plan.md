---
title: "feat: Messaging Phase 1 foundations (retry + OTel + capability docs + strict-tenancy guard)"
type: feat
status: active
date: 2026-05-03
origin: https://github.com/xshaheen/headless-framework/issues/217
parent_epic: https://github.com/xshaheen/headless-framework/issues/217
issues:
  - https://github.com/xshaheen/headless-framework/issues/229
  - https://github.com/xshaheen/headless-framework/issues/230
  - https://github.com/xshaheen/headless-framework/issues/231
  - https://github.com/xshaheen/headless-framework/issues/238
---

# feat: Messaging Phase 1 foundations (retry + OTel + capability docs + strict-tenancy guard)

## Overview

Land the remaining additive Phase 1 foundations of the `Headless.Messaging` epic now that the
`TenantId` envelope (U2 / #228) has shipped: a uniform `IRetryBackoffStrategy` dispatch wiring
across every provider, an `IActivityTagEnricher` hook on the OpenTelemetry package, a capability
matrix doc + envelope reference under `docs/llms/`, and the strict-tenancy publish guard
(#238) that pairs with the new envelope contract. None of these change consumer call-sites — they
add typed seams, defaults, and documentation on top of today's `IDirectPublisher` /
`IOutboxPublisher` shape.

The publisher-interface rename (U1 / U1b / U3a-c) and the NATS-ergonomics work (U7 / U8 / U9)
remain deferred per the Phase 1 scope reconciliation in #217.

## Problem Frame

Three of the four Phase 1 units in the canonical spec at
[`specs/2026-04-19-001-messaging-feature-spec.md`](../../specs/2026-04-19-001-messaging-feature-spec.md)
are still open:

- **U4 (#229)** — `IRetryBackoffStrategy` is defined, has a default `ExponentialBackoffStrategy`,
  and is **already wired through the central consume pipeline** at
  `Headless.Messaging.Core/Internal/SubscribeExecutor.cs:48` (`_backoffStrategy = options.Value.RetryBackoffStrategy`).
  Providers do **not** reimplement retry — they hand messages to `IConsumerRegister.OnMessageCallback`,
  which delegates to `SubscribeExecutor.ExecuteAsync` (line 109+), and that path already calls
  `ShouldRetry` (line 208) + `GetNextDelay` (line 241). The `_NextBackoff` loops in `NatsConsumerClient`
  and similar are **broker-reconnection** backoff for `NextAsync`-style transport API errors, not
  message-dispatch retry.
  The actual gap is calibration, not wiring: today's `MessagingOptions.RetryBackoffStrategy` is
  shared across (a) in-flight transient consumer-side failures (where ~5 attempts over ~3s is
  appropriate) and (b) the durable `MessageNeedToRetryProcessor` storage-backed redelivery loop
  (where ~50 attempts over hours is appropriate — note today's `FailedRetryCount = 50` default).
  U4 ships a separate `RetryBackoffOptions` for the in-flight path, refactors
  `ExponentialBackoffStrategy` to consume it, and renames the existing
  `MessagingOptions.RetryBackoffStrategy` to disambiguate.
- **U5 (#230)** — `Headless.Messaging.OpenTelemetry`'s `DiagnosticListener` stamps a hand-coded
  set of attributes (`messaging.destination.name`, propagation context). There is no extensibility
  seam for consumer apps to attach domain tags — every team forks the package or wraps activities
  manually. With `TenantId` now on the envelope (U2), the package should emit
  `headless.messaging.tenant_id` and other Headless-axis attributes by default and let consumers
  layer their own enrichers without forking.
- **U6 (#231)** — `docs/llms/messaging.md` exists but does not document the publisher capability
  matrix, the cross-provider retry contract introduced in U4, the new envelope shape from U2, or
  a convention-axis operational runbook. The abstractions README and provider READMEs do not
  state which interfaces each transport supports. Consumers cannot pick a transport from the
  docs alone.
- **#238** — A sibling of the EF write guard (#234) and the Mediator behavior (#236): when an
  ambient `ICurrentTenant` is required but absent at publish, the framework should reject the
  publish with `MissingTenantContextException` rather than silently emitting a
  `TenantId = null` envelope. Originally bundled in U2 but split out 2026-05-01 because the
  failure semantics differ from #228's header-injection check (which already shipped in
  `MessagePublishRequestFactory._ApplyTenantId`).

## Requirements

Carried forward from the origin spec §Requirements Trace; only the requirements active in this
plan are listed.

- **R5.** `IRetryBackoffStrategy.ShouldRetry` and `GetNextDelay` are the single decision point for
  "retry vs DLQ" in **every** provider's consumer dispatch loop, not just a subset.
- **R6.** OpenTelemetry spans emitted by `Headless.Messaging.OpenTelemetry` are extensible via a
  tag-enricher hook. The default enricher emits `headless.messaging.tenant_id` (when set),
  `headless.messaging.delivery_kind` (Phase 1: always `"send"` because Send/Broadcast split is
  Phase 2), and the OTel-standardized `messaging.system` / `messaging.operation.type` /
  `messaging.destination.name` per provider.
- **R9.** Public XML docs and package READMEs stay in sync with the new shape; a single capability
  matrix doc at `docs/llms/messaging-envelope.md` is the source of truth, including the
  publish-time failure-code list (`ReservedTenantHeader`, `TenantIdMismatch`,
  `MissingTenantContext`) inherited from U2 + U10.
- **#238 / R4 supplementary.** When a host is configured with `TenantContextRequired = true`, a
  publish call that resolves no `TenantId` (neither `PublishOptions.TenantId` nor
  `ICurrentTenant.Id`) is rejected at the publish wrapper with `MissingTenantContextException`,
  matching the semantics of the EF write guard (#234) and the Mediator behavior (#236).

## Scope Boundaries

- **No publisher-interface rename.** `IDirectPublisher` / `IOutboxPublisher` remain the public
  publisher seams. The Send/Broadcast split (U1) lands in Phase 2.
- **No outbox decorator.** `OutboxPublisherDecorator<TTransport>`, `IOutboxStore`, and
  `services.AddOutbox<TTransport>()` are Phase 2 (U1b). The drainer-side
  `OutboxRedispatchBackoffOptions` is therefore **not** introduced in this plan; only the
  consumer-dispatch `RetryBackoffOptions` from U4 ships.
- **No NATS-ergonomics work.** `StreamAutoCreationMode`, `IDeadLetterObserver`, declarative
  stream router (U7-U9) are post-Phase-2.
- **No consumer-side enricher split.** `IConsume<T>` / `ConsumeContext<T>` stay exactly as they
  are. The OTel enricher operates on activities, not on consumer surface types.
- **No always-on tenant authentication.** Per R4 trust model and the Security Considerations
  section of #217: the framework trusts the publisher and does not verify tenant authenticity on
  consume. The strict-tenancy publish guard (#238) is a publish-side absence check; it does not
  cryptographically validate that the typed `TenantId` matches an out-of-band signal. Consumer
  apps that need cross-tenant authenticity layer their own `IConsumeBehavior<T>` in Phase 2.
- **No header / exception-message scrubbing in OTel.** The `IActivityTagEnricher` hook reads
  raw `Headers` and (in user-supplied enrichers) may stamp exception messages onto spans.
  Header / message scrubbing is the consumer app's responsibility in Phase 1; a built-in
  scrubber ships in Phase 2 alongside `DeadLetterEventScrubOptions` (U8). U5 documents
  the constraint as a callout in `Headless.Messaging.OpenTelemetry/README.md` and in U6.
- **No OTel metrics additions.** U5 modifies span tags only. The existing `MessagingMetrics`
  source-generated counters (publish/consume duration, persistence duration) are unchanged.
  Counters tagged by retry verdict (`success | retry | dead_letter`) are deferred to a
  follow-up — flagged in the plan but not implemented here. Rationale: keeping U5 scoped to
  the enricher hook avoids coupling span-tag changes to metric-shape changes that vendor
  dashboards bind to.
- **No `DeliveryKind` enum on `ConsumeContext`.** That property is part of U1 (Phase 2). The
  `headless.messaging.delivery_kind` OTel attribute in U5 emits the static value `"send"` in
  Phase 1 and is widened in Phase 2.

### Deferred to Separate Tasks

- **Per-provider migration of `IDirectPublisher` → `IDirectSendPublisher`**: Phase 2 (U3a / U3b
  / U3c).
- **`MessageNeedToRetryProcessor` parity rework**: the existing failed-message retry processor
  in `Headless.Messaging.Core/Processor/IProcessor.NeedRetry.cs` reads from durable storage and
  is orthogonal to U4's in-flight dispatch retry. Aligning its options shape with the new
  `RetryBackoffOptions` is a separate maintenance task tracked outside this plan.
- **`IDeadLetterObserver`**: U8, post-Phase-2. U4 falls through to whatever each provider's
  current DLQ/parking mechanism is when `ShouldRetry` returns false.

## Context & Research

### Relevant Code and Patterns

- [`src/Headless.Messaging.Abstractions/IRetryBackoffStrategy.cs`](../../src/Headless.Messaging.Abstractions/IRetryBackoffStrategy.cs)
  — current surface: `TimeSpan? GetNextDelay(int retryAttempt, Exception?)` and
  `bool ShouldRetry(Exception)`. **Already shipped — not modified by U4.**
- [`src/Headless.Messaging.Core/Retry/ExponentialBackoffStrategy.cs`](../../src/Headless.Messaging.Core/Retry/ExponentialBackoffStrategy.cs)
  — default exponential strategy with ±25% jitter, 1s initial / 5min cap / 2x multiplier.
- [`src/Headless.Messaging.Core/Retry/FixedIntervalBackoffStrategy.cs`](../../src/Headless.Messaging.Core/Retry/FixedIntervalBackoffStrategy.cs)
  — fixed-interval alternative.
- [`src/Headless.Messaging.Core/Configuration/MessagingOptions.cs`](../../src/Headless.Messaging.Core/Configuration/MessagingOptions.cs)
  — exposes `RetryBackoffStrategy` (line 187), `FailedRetryInterval`, `FailedRetryCount`,
  `RetryProcessor`. **Renamed** in U4: `RetryBackoffStrategy` → `OutboxRetryBackoffStrategy`,
  `FailedRetryCount` → `OutboxFailedRetryCount`, `FailedRetryInterval` →
  `OutboxFailedRetryInterval` per greenfield posture in `CLAUDE.md` ("Prefer simpler, cleaner
  APIs even when that requires breaking changes"). The new `RetryBackoffOptions` carries
  in-flight-dispatch defaults; the renamed `Outbox*` properties drive the durable
  `MessageNeedToRetryProcessor` storage-backed redelivery.
- [`src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs`](../../src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs)
  — central retry pipeline (`SubscribeExecutor.ExecuteAsync`, line 109+). Today reads
  `options.Value.RetryBackoffStrategy` once at construction (line 48). U4 refactors to read
  `IOptions<RetryBackoffOptions>` and consume strategy via the renamed property when storage
  redelivery applies.
- [`src/Headless.Messaging.Core/Internal/IMessagePublishRequestFactory.cs`](../../src/Headless.Messaging.Core/Internal/IMessagePublishRequestFactory.cs)
  — `MessagePublishRequestFactory._ApplyTenantId` is the strict 4-case header integrity policy
  shipped by U2/#228 and is the host for the #238 strict-tenancy absence check (U10).
- Per-provider consumer client classes (no per-provider retry edits required — retry runs
  through the shared `SubscribeExecutor`):
  - [`src/Headless.Messaging.Nats/NatsConsumerClient.cs`](../../src/Headless.Messaging.Nats/NatsConsumerClient.cs)
    — `_NextBackoff` is broker-reconnection backoff for `consumer.NextAsync` errors, not
    message-dispatch retry; left untouched.
  - Other providers (`RabbitMqBasicConsumer`, `AzureServiceBusConsumerClient`,
    `KafkaConsumerClient`, etc.) — call `OnMessageCallback` and let the central pipeline
    handle retry; left untouched.
- [`src/Headless.Messaging.OpenTelemetry/DiagnosticListener.cs`](../../src/Headless.Messaging.OpenTelemetry/DiagnosticListener.cs)
  — current span instrumentation (~19KB). U5 modifies it to consult registered enrichers.
- [`src/Headless.Messaging.OpenTelemetry/Setup.cs`](../../src/Headless.Messaging.OpenTelemetry/Setup.cs)
  — `AddMessagingInstrumentation()` fluent API. U5 extends it with `AddActivityTagEnricher<T>()`.
- [`src/Headless.Core/Abstractions/ICurrentTenant.cs`](../../src/Headless.Core/Abstractions/ICurrentTenant.cs)
  — ambient tenant accessor used by U10. "Ambient tenant" is used throughout this plan to
  refer to the `ICurrentTenant` value resolved from the publish-call's execution context.
- [`src/Headless.Hosting/Options/OptionsBuilderFluentValidationExtensions.cs`](../../src/Headless.Hosting/Options/OptionsBuilderFluentValidationExtensions.cs)
  — `ValidateFluentValidation<TOptions>()` extension. All new options classes in this plan use
  this pattern per `CLAUDE.md`.
- [`tests/Headless.Messaging.Core.Tests.Unit/Configuration/MessagingOptionsValidationTests.cs`](../../tests/Headless.Messaging.Core.Tests.Unit/Configuration/MessagingOptionsValidationTests.cs)
  — reference validator-test layout.

### Institutional Learnings

- Re-run `learnings-researcher` at `dev:code` kickoff for any newly-captured messaging decisions
  in `docs/solutions/`.
- Greenfield breaking-change posture (`CLAUDE.md`): no migration shims for renamed options or
  removed defaults. The default `RetryBackoffOptions` shape is identical in spirit to today's
  `ExponentialBackoffStrategy` defaults — but where defaults change, the change is documented in
  U6 release notes rather than gated by a compatibility flag.
- FluentValidation + `services.AddOptions<T, TValidator>().ValidateOnStart()` is the canonical
  options pattern (`CLAUDE.md`); no bespoke `IHostedService`, no custom exception type — failures
  surface as `OptionsValidationException`.
- Prior plan: [`docs/plans/2026-05-01-001-feat-tenant-id-envelope-plan.md`](2026-05-01-001-feat-tenant-id-envelope-plan.md)
  (status: completed) — establishes the envelope vocabulary U10 and U5 build on.

### External References

- MassTransit retry layering (`UseMessageRetry` + `UseRetry`): <https://masstransit.io/documentation/configuration/retry>
  — three-layer override pattern (global / options / per-message-type) is the model for U4.
- OpenTelemetry messaging semantic conventions: <https://opentelemetry.io/docs/specs/semconv/messaging/>
  — `messaging.operation.type`, `messaging.system`, `messaging.destination.name` enum values.
  The `headless.messaging.*` prefix is used for all custom attributes per OTel's
  attribute-naming spec recommendation that third-party libraries avoid existing semconv
  namespaces.
- W3C Trace Context: <https://www.w3.org/TR/trace-context/> — propagation already in place;
  U5 leaves it untouched.

### Cross-Provider Retry Contract (Definitions)

These definitions land in U6 documentation and are enforced by U4 tests on `SubscribeExecutor`
before any provider integration tests run.

- **Attempt** = one consumer invocation that returned or threw. The attempt counter is an
  **in-process** value held by `SubscribeExecutor` and keyed by `(MessageId, instance)`. Every
  `IRetryBackoffStrategy` call sees this counter, not a transport-native delivery count.
- **`Headers.Attempt` is per-instance, not globally monotonic.** Phase 1 does **not** attempt
  to mutate `Headers.Attempt` on the wire across requeue-with-delay paths because the surveyed
  transports (RabbitMQ `BasicNack(requeue)`, NATS `Nak`, Pulsar negative-ack, SQS
  `ChangeMessageVisibility`, Redis Streams `XCLAIM`) all redeliver the original payload
  unchanged. Mutating the header would require re-publishing under a different `MessageId`,
  which would defeat downstream dedup.
  Instead: `SubscribeExecutor` reads any inbound `headless-attempt` value as the **observed**
  starting point (used for diagnostics + OTel emission), increments its in-process counter on
  every invocation, and writes the in-process value into `Headers.Attempt` before invoking the
  consumer. After lease expiry on a peer instance, the counter restarts from the inbound wire
  value — this is provider-defined behavior documented per-transport in U6.
- **Publishers do not stamp `Headers.Attempt`.** The publish factory's `_ReservedHeaders` set
  rejects caller-supplied values (U4 adds `Headers.Attempt` to the set). Asserted by a publish
  guard test in U4.
- **Replay reset semantics.** Manually replayed messages from a DLQ to the main queue typically
  carry a stale `headless-attempt` value. Because the counter is per-instance, the strategy
  observing a stale inbound value still increments from there in the new instance — operators
  who want a clean retry budget on replay strip the header at replay time. U6 documents
  per-provider replay recipes (RabbitMQ shovel, ASB resubmit, SQS redrive).
- **Delay source** = app-enforced when `GetNextDelay` returns a non-null `TimeSpan`;
  transport-enforced only when the strategy explicitly returns `null` and the storage processor
  takes over.
- **Immediate retry** = in-memory retry on the same consumer instance via `Task.Delay`; does
  not write to durable storage. Bounded by `RetryBackoffOptions.MaxAttempts` (Phase 1 default
  `5`).
- **Storage redelivery** = `MessageNeedToRetryProcessor` reads the durable retry table on its
  own polling cadence using `MessagingOptions.OutboxRetryBackoffStrategy` (renamed from
  `RetryBackoffStrategy`) + `OutboxFailedRetryCount`. Untouched by U4 except for the rename.
- **DLQ fall-through** = when `ShouldRetry(exception)` returns false **or** in-memory retries
  exceed `RetryBackoffOptions.MaxAttempts` and the message is not eligible for storage
  redelivery, the consumer's current dead-letter pathway runs. Phase 1 does **not** introduce
  a unified `IDeadLetterObserver`.

## Key Technical Decisions

1. **`RetryBackoffOptions` is the single tunable surface for in-flight dispatch retry.**
   Default: `MaxAttempts = 5`, `BaseDelay = 100ms`, `MaxDelay = 30s`, `JitterRatio = 0.25`,
   `NonRetryableExceptionTypes` includes `OperationCanceledException` plus consumer-supplied
   types. Validated via FluentValidation + `ValidateOnStart()`. Bound from
   `Headless:Messaging:RetryBackoff` configuration section. The renamed
   `MessagingOptions.OutboxRetryBackoffStrategy` continues to drive the **failed-message
   storage processor** (`MessageNeedToRetryProcessor`) — the two paths share the algorithm
   class but read different options. The rename disambiguates the two surfaces at the API
   level.
2. **Two-layer retry override surface, validated at startup.**
   - Layer 1: `services.AddSingleton<IRetryBackoffStrategy, MyStrategy>()` replaces the default
     globally.
   - Layer 2: `services.Configure<RetryBackoffOptions>(o => ...)` tunes the default
     `ExponentialBackoffStrategy` without rewriting it.
   The previously planned per-message-type `IRetryBackoffStrategy<TMessage>` (Layer 3) is
   **deferred** — no Phase 1 use case requires it, and adding a generic interface to
   `Headless.Messaging.Abstractions` now locks public API. The dispatcher leaves room for
   a typed-keyed resolver in Phase 2 if a concrete consumer scenario surfaces.
3. **Retry runs through the existing central pipeline (`SubscribeExecutor`).** Phase 1 does
   **not** add a per-provider dispatcher. The existing
   `Headless.Messaging.Core/Internal/SubscribeExecutor.ExecuteAsync` already drives the retry
   verdict via `IRetryBackoffStrategy.ShouldRetry` + `GetNextDelay` for every provider — the
   gap is calibration, not wiring. U4 extends `SubscribeExecutor` to read
   `IOptions<RetryBackoffOptions>` for in-flight retry while the renamed
   `MessagingOptions.OutboxRetryBackoffStrategy` continues to drive durable storage
   redelivery via `MessageNeedToRetryProcessor`. Providers do not change.
4. **OTel `IActivityTagEnricher` is invoked from `DiagnosticListener` after the existing core
   tags are stamped.** `DefaultActivityTagEnricher` is registered first by
   `AddMessagingInstrumentation` and a startup invariant test asserts it is first in the
   resolved enricher sequence. Custom enrichers run in DI registration order. The previously
   planned `OpenTelemetryMessagingOptions.EnricherOrder` (`List<Type>`) is dropped —
   registration order matches `IConsumerLifecycle` convention already in this codebase and
   avoids a `Type`-list validator that resolves DI types from inside an options validator.
5. **`headless.messaging.*` attribute namespace.** All custom Headless attributes use the
   reverse-domain `headless.messaging.*` prefix per OTel attribute-naming guidance. OTel-
   standardized attributes (`messaging.operation.type`, `messaging.system`,
   `messaging.destination.name`) keep canonical names so vendor dashboards (Honeycomb, Datadog,
   Aspire, Grafana Tempo) light up out of the box.
6. **`messaging.system` is omitted when no OTel-registered enum value applies.** NATS uses the
   not-yet-registered `"nats"` value; in-memory transports, the in-process queue, and the SQL
   transports omit `messaging.system` entirely (no fabricated string). Documented in U6.
7. **Tenant-tag suppression for cross-tenant trace storage.**
   `OpenTelemetryMessagingOptions.SuppressTenantIdTag` (default `false`) gates emission of
   `headless.messaging.tenant_id` on publish/consume spans. When `true`, the default enricher
   omits the tag entirely (no hashing — partial leakage through hashes is worse than absence).
   Operators set this when their trace backend is shared across tenants and tenant identity is
   itself sensitive. **`SuppressTenantIdTag = true` also redacts tenant-bearing fields from
   known framework exceptions before they reach `Activity.AddException`** — see U5 §Approach
   for the exception-redaction contract. Without that redaction, exception messages from
   `MissingTenantContextException` and the U2 `TenantIdMismatch` path would leak tenant
   identifiers through `exception.message` regardless of the flag.
8. **Strict-tenancy guard semantics (U10 / #238).**
   - `MessagingOptions.TenantContextRequired = false` (default in Phase 1): publish is
     permitted with `TenantId = null`; the wrapper does not consult `ICurrentTenant`.
     Rationale: sibling guards #234 (EF write guard) and #236 (Mediator behavior) are not
     yet shipped — there is no defaulting alignment to enforce. When the sibling guards
     ship, the three defaults are revisited together; the cross-layer story is consistent
     "off-by-default until the operator opts in across all three." Greenfield posture in
     `CLAUDE.md` permits flipping the default in a later release once sibling defaults are
     known.
   - `TenantContextRequired = true`: at publish time, if `PublishOptions.TenantId` is null,
     the wrapper resolves `ICurrentTenant.Id`. If both are null, throw
     `MissingTenantContextException` (a new type in `Headless.Messaging.Abstractions/Exceptions`).
     If `ICurrentTenant.Id` is non-null and `PublishOptions.TenantId` is null, the wrapper
     stamps the resolved tenant onto the header (preserving the four-case integrity invariant
     from U2: typed and raw header are reconciled identically to `_ApplyTenantId`).
   - The guard runs **after** the existing 4-case integrity check from U2 — header injection
     and tenant absence are surfaced as distinct failure codes:
     `Data["Headless.Messaging.FailureCode"] = "ReservedTenantHeader"` (U2),
     `"TenantIdMismatch"` (U2), `"MissingTenantContext"` (U10).
9. **`ICurrentTenant` is consumed via DI, never via static accessors.** Both
   `MessagePublishRequestFactory` and `ICurrentTenant` are registered **singleton**
   (current pattern at `Headless.Messaging.Core/Setup.cs:96` and `Headless.Api/Setup.cs:166`).
   `ICurrentTenant` is wired to `AsyncLocalCurrentTenantAccessor` so per-call resolution
   reads from the publish scope's flow context — no factory-lifetime change is required for
   U10. When `Headless.Core` is not registered, `NullCurrentTenant` is the fallback and
   `TenantContextRequired = true` becomes a startup-time contradiction caught by
   `MessagingOptionsValidator` (U10 augments the existing validator).
10. **Documentation lives in `docs/llms/messaging-envelope.md` and is referenced from every
    provider README + `Headless.Messaging.Abstractions/README.md`.** The existing
    `docs/llms/messaging.md` is left intact and grows a "see also" cross-link; the envelope doc
    is the focused reference for envelope shape, capability matrix, retry contract, and the
    convention-axis runbook.

## Open Questions

### Resolved During Planning

- *Does U4 modify `MessagingOptions.RetryBackoffStrategy`?* It is **renamed** to
  `OutboxRetryBackoffStrategy` (greenfield breaking change per `CLAUDE.md`); behavior is
  unchanged — it continues to drive `MessageNeedToRetryProcessor` (failed-message storage
  retry). U4 introduces a separate `RetryBackoffOptions` for in-flight dispatch retry.
  The two share the strategy class but read different options. Sharing a single options
  type was rejected because failed-message-storage retry is calibrated for
  transport-availability failures (broker down for minutes-to-hours) while in-flight dispatch
  retry is calibrated for transient consumer-side failures (5 attempts over ~3s). See origin
  spec U4 §"Two distinct backoff option types".
- *Where does the strict-tenancy guard run?* In `MessagePublishRequestFactory._ApplyTenantId`
  (extending the existing U2 method), so providers inherit it without per-provider duplication.
  U10 extends `_ApplyTenantId` and adds a sibling `_ResolveAmbientTenant` step.
- *Is `headless.messaging.delivery_kind` emitted in Phase 1?* Yes, with the static value
  `"send"`. Phase 2 widens it to `"send" | "broadcast"` once `ConsumeContext.DeliveryKind`
  exists. Emitting the tag in Phase 1 lets vendor dashboards reserve the column up front.
- *Does the Send vs Broadcast OTel value require Phase 2 first?* No — emitting `"send"`
  unconditionally in Phase 1 is forward-compatible with the Phase 2 widening. The alternative
  (omit the tag in Phase 1, add it in Phase 2) would change vendor dashboards mid-flight.

### Deferred to Implementation

- **Whether `DefaultActivityTagEnricher` reads `IOptionsMonitor<OpenTelemetryMessagingOptions>`
  or snapshots options at construction.** Lean: `IOptionsMonitor` so `SuppressTenantIdTag` can
  be flipped at runtime without restart.
- **Per-instance retry budget when `RetryBackoffOptions.MaxAttempts` is exceeded but no native
  DLQ primitive exists** (e.g., InMemoryQueue). Lean: route to durable retry table per
  today's behavior; if no durable storage is configured, log at warning + drop. Documented
  in U6 alongside the retry contract.
- **Whether `MissingTenantContextException` lives in `Headless.Messaging.Abstractions` or
  `Headless.Messaging.Core`.** Lean: `Abstractions` so consumer apps can `catch` it without
  a `Core` dependency. Mirrors the `Headless.Checks` pattern of throwing well-known exception
  types from abstractions.
- **Exact set of `_ReservedHeaders` additions.** Phase 1 adds `Headers.Attempt`. Whether
  the U2-shipped `Headers.TenantId` should also be added (it currently flows through
  `_ApplyTenantId` rather than `_ValidateCustomHeaders`) is implementer's choice — both
  paths produce the same reserved-header guarantee.

## High-Level Technical Design

Directional only. Pseudo-shapes — implementation-time names may differ.

### Retry dispatch flow (U4)

```text
Provider.OnMessage(envelope)                  // unchanged
  -> IConsumerRegister.OnMessageCallback      // unchanged
       -> SubscribeExecutor.ExecuteAsync(message, ct)   // EXTENDED in U4
            inProcessAttempt = perInstanceCounter[message.MessageId] ?? 0
            for (i = 0; i < RetryBackoffOptions.MaxAttempts; i++)
              attempt = ++inProcessAttempt
              headers[Headers.Attempt] = attempt
              try { await consumer.Consume(ctx, ct); ack(); return; }
              catch (Exception ex)
                if (ex is OperationCanceledException && ct.IsCancellationRequested) throw
                if (!strategy.ShouldRetry(ex)) break                  // route to durable retry table
                if (RetryBackoffOptions.NonRetryableExceptionTypes.Any(t => t.IsInstanceOfType(ex))) break
                delay = strategy.GetNextDelay(attempt, ex)
                if (delay is null) break
                if (delay <= RetryBackoffOptions.ImmediateRetryThreshold)
                  await Task.Delay(delay, ct)                          // in-process retry
                else
                  scheduleRequeueViaProviderNativePrimitive(delay); return
            // budget exhausted or non-retryable: durable retry table OR DLQ fall-through
            await storeForFailedRetry(message)
            // MessageNeedToRetryProcessor (out-of-band) drives storage redelivery using
            // the renamed MessagingOptions.OutboxRetryBackoffStrategy + OutboxFailedRetryCount
```

### OTel enricher composition (U5)

```text
DiagnosticListener.OnNext(publish-event)
  -> activity = source.StartActivity("messaging.publish", ActivityKind.Producer)
  -> stamp core tags: messaging.system, messaging.destination.name, messaging.operation.type = "send"
  -> tagContext = new MessageTagContext(envelope, deliveryKind: "send")
  -> foreach enricher in capturedServiceProvider.GetServices<IActivityTagEnricher>():  // DI registration order
       try { enricher.Enrich(activity, tagContext); }
       catch (Exception ex) { log warning, continue; }
  -> if exception is not null:
       if (options.SuppressTenantIdTag && KnownFrameworkExceptionRedactor.IsKnown(exception))
         KnownFrameworkExceptionRedactor.StampRedacted(activity, exception)
       else
         activity.AddException(exception)
  -> activity.Stop()
```

### Strict-tenancy guard (U10 / #238)

```text
MessagePublishRequestFactory._ApplyTenantId  (after U2 4-case check)
  if (options.TenantContextRequired)
    if (typed is null && ambient = ICurrentTenant.Id is null)
      throw new MissingTenantContextException(
        "Publish requires an ambient tenant context but none was set. " +
        "Set PublishOptions.TenantId explicitly or wrap the publish in ICurrentTenant.Change(...).");
    if (typed is null && ambient is not null)
      typed = ambient;                                  // re-enter U2 flow with resolved value
```

## Output Structure

```text
src/Headless.Messaging.Abstractions/
  Exceptions/MissingTenantContextException.cs     # new (U10)
  IRetryBackoffStrategy.cs                         # unchanged

src/Headless.Messaging.Core/
  Configuration/MessagingOptions.cs                # + TenantContextRequired (U10)
  Configuration/MessagingOptionsValidator.cs       # extend or create (U10 cross-check)
  Internal/IMessagePublishRequestFactory.cs        # + ambient tenant resolution (U10)
  Retry/RetryBackoffOptions.cs                     # new (U4) + same-file Validator
  Retry/ExponentialBackoffStrategy.cs              # extended (U4) — new ctor reading IOptions<RetryBackoffOptions>
  Internal/SubscribeExecutor.cs                    # extended (U4) — read in-flight options + per-instance counter
  Configuration/MessagingOptions.cs                # rename (U4): RetryBackoffStrategy → OutboxRetryBackoffStrategy, etc.

# Per-provider directories: NO source edits required by U4. All 11 providers already
# delegate to IConsumerRegister.OnMessageCallback → SubscribeExecutor.ExecuteAsync, which
# is the only file touched by retry calibration. Provider-specific operator constraints
# (Kafka heartbeat, ASB lock duration, NATS ack_wait, SQS visibility, etc.) are documented
# in U6.

src/Headless.Messaging.OpenTelemetry/
  IActivityTagEnricher.cs                          # new (U5)
  MessageTagContext.cs                             # new (U5) — sealed record, no interface
  DefaultActivityTagEnricher.cs                    # new (U5)
  OpenTelemetryMessagingOptions.cs                 # new (U5) — SuppressTenantIdTag only
  OpenTelemetryMessagingOptionsValidator.cs        # new (U5)
  Setup.cs                                         # + AddActivityTagEnricher<T>() (U5)
  DiagnosticListener.cs                            # invoke registered enrichers (U5)
  MessagingInstrumentation.cs                      # capture IServiceProvider for enricher resolution (U5)

docs/llms/
  messaging-envelope.md                            # new (U6)
  messaging.md                                     # + cross-link to envelope doc (U6)

src/Headless.Messaging.Abstractions/README.md      # capability matrix link (U6)
src/Headless.Messaging.<Provider>/README.md        # capability statement per provider (U6)
```

## Implementation Units

Four units. U4 is a single unit (no per-provider sub-units) because the central pipeline
already runs retry — the work is contract calibration in `Headless.Messaging.Core`. U-IDs `U4`,
`U5`, `U6` mirror the canonical spec; `U10` is plan-local for the #238 strict-tenancy guard
(sibling of the spec's already-shipped U2). U-IDs are stable across the lifetime of this plan
and never renumbered.

---

- U4. **Calibrate the central retry pipeline for in-flight transient failures**

**Goal:** Refit `SubscribeExecutor` to run on a calibrated `RetryBackoffOptions` surface
distinct from the durable-storage `MessageNeedToRetryProcessor` knobs. Disambiguate the
existing `MessagingOptions.RetryBackoffStrategy` / `FailedRetryCount` / `FailedRetryInterval`
properties by renaming them with an `Outbox` prefix. No per-provider edits — providers already
hand to the central pipeline through `IConsumerRegister.OnMessageCallback`.

**Requirements:** R5.

**Dependencies:** None. U2 already shipped.

**Files:**
- Create: `src/Headless.Messaging.Core/Retry/RetryBackoffOptions.cs` (+ same-file
  `RetryBackoffOptionsValidator : AbstractValidator<RetryBackoffOptions>`).
- Modify: `src/Headless.Messaging.Core/Retry/ExponentialBackoffStrategy.cs` — add a constructor
  that consumes `IOptions<RetryBackoffOptions>` (in addition to the existing
  `(initialDelay, maxDelay, multiplier)` constructor used by `MessageNeedToRetryProcessor`).
  Move the hard-coded `ShouldRetry` exception list into a documented default that
  `RetryBackoffOptions.NonRetryableExceptionTypes` augments rather than replaces.
- Modify: `src/Headless.Messaging.Core/Configuration/MessagingOptions.cs` — rename
  `RetryBackoffStrategy` → `OutboxRetryBackoffStrategy`, `FailedRetryCount` →
  `OutboxFailedRetryCount`, `FailedRetryInterval` → `OutboxFailedRetryInterval` (greenfield
  posture; no compatibility shims). Register `RetryBackoffOptions` via
  `AddOptions<RetryBackoffOptions, RetryBackoffOptionsValidator>().ValidateOnStart()` in the
  messaging bootstrapper.
- Modify: `src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs` — switch the in-flight
  retry path from `options.Value.RetryBackoffStrategy` (line 48) to a `IOptions<RetryBackoffOptions>`-
  parameterized strategy. Read `RetryBackoffOptions.MaxAttempts` instead of the renamed
  `OutboxFailedRetryCount` for the in-memory retry budget. The storage-redelivery branch
  (`message.Retries >= _options.OutboxFailedRetryCount`) keeps reading the renamed Outbox
  property.
- Modify: `src/Headless.Messaging.Abstractions/Headers.cs` — add `const string Attempt =
  "headless-attempt"` constant.
- Modify: `src/Headless.Messaging.Core/Internal/IMessagePublishRequestFactory.cs` — add
  `Headers.Attempt` to `_ReservedHeaders` so callers cannot poison the counter at publish
  time.
- Test: `tests/Headless.Messaging.Core.Tests.Unit/Retry/RetryBackoffOptionsValidatorTests.cs`
- Test: `tests/Headless.Messaging.Core.Tests.Unit/Retry/RetryContractTests.cs` — locks down
  the in-process attempt counter, the cancellation-vs-timeout distinction, the
  reserved-header guard, and the `MaxAttempts` boundary.
- Test: `tests/Headless.Messaging.Core.Tests.Unit/SubscribeExecutorRetryTests.cs` (extend the
  existing file) — assert the new options surface drives in-flight retry while the renamed
  `Outbox*` properties drive storage redelivery.

**Approach:**
- `RetryBackoffOptions` carries `MaxAttempts` (default `5`), `BaseDelay` (default `100ms`),
  `MaxDelay` (default `30s`), `JitterRatio` (default `0.25`), `ImmediateRetryThreshold`
  (default `250ms` — delays at or below this run in-process via `Task.Delay`), and
  `NonRetryableExceptionTypes : IList<Type>` (defaults: empty list — augments the existing
  strategy hardcoded set documented in `ExponentialBackoffStrategy`).
- Validator rules: `MaxAttempts >= 1`, `BaseDelay > 0`, `MaxDelay >= BaseDelay`,
  `JitterRatio in [0, 1]`, `ImmediateRetryThreshold >= 0`.
- `ExponentialBackoffStrategy` gains a second constructor taking `IOptions<RetryBackoffOptions>`.
  Two registered instances co-exist under DI: the in-flight one consumed by `SubscribeExecutor`,
  and the existing one wired into `MessagingOptions.OutboxRetryBackoffStrategy` for
  `MessageNeedToRetryProcessor`. Same algorithm, two parameterizations, no class duplication.
- `SubscribeExecutor.ExecuteAsync` reads the in-flight strategy + `RetryBackoffOptions.MaxAttempts`
  for the in-memory retry loop. When the in-memory budget exhausts, the message moves to the
  durable retry table (existing behavior, unchanged) and the `OutboxRetryBackoffStrategy` +
  `OutboxFailedRetryCount` path takes over for the redelivery cadence.
- **`Headers.Attempt` is per-instance, not globally monotonic** (see Cross-Provider Retry
  Contract Definitions above). `SubscribeExecutor` keeps an in-process counter keyed by
  `MessageId` for the lifetime of the in-memory retry loop, writes the value to
  `Headers.Attempt` before each consumer invocation, and reads any inbound wire value as the
  observed starting point. After lease expiry on a peer instance the counter restarts — this
  is provider-defined behavior and is documented in U6.
- **Cancellation vs timeout distinction.** `OperationCanceledException` whose
  `CancellationToken == ct` (the executor's host-shutdown token, or any token derived from
  it) is **never retried** — short-circuit before consulting `ShouldRetry`. `OperationCanceledException`
  from an unrelated token (per-call HTTP timeout, library-specific timeout) is treated as any
  other exception and consults `ShouldRetry`. The check uses
  `ex.CancellationToken == ct || ct.IsCancellationRequested` so derived linked tokens still
  flow through the shutdown path.
- **No per-provider edits.** All 11 providers already delegate to `IConsumerRegister.OnMessageCallback`
  → `SubscribeExecutor.ExecuteAsync`. The retry verdict ships once at the central pipeline.
  The only per-provider documentation deliverable is the U6 audit note describing how each
  transport's native primitives (Ack/Nack/visibility/lease) interact with the in-memory retry
  loop — see U6.
- **Lock/lease vs `MaxDelay` validation.** Only `Headless.Messaging.AzureServiceBus` exposes a
  typed timeout field today (`SubscriptionMessageLockDuration` in `AzureServiceBusOptions`).
  U4 adds an ASB-only startup validator that asserts
  `SubscriptionMessageLockDuration > RetryBackoffOptions.MaxDelay + ImmediateRetryThreshold + 5s`.
  For NATS / Pulsar / Kafka / SQS, U6 documents the constraint as operator responsibility
  with the formula and worked examples per provider. Adding typed timeout fields to those
  providers' options classes is Phase 2 work, not blocking Phase 1.

**Patterns to follow:**
- Existing `ExponentialBackoffStrategy` shape — same algorithm, second constructor.
- `OptionsBuilderFluentValidationExtensions.ValidateFluentValidation<TOptions>()` for
  registration.
- Test scaffolding from
  [`tests/Headless.Messaging.Core.Tests.Unit/Configuration/MessagingOptionsValidationTests.cs`](../../tests/Headless.Messaging.Core.Tests.Unit/Configuration/MessagingOptionsValidationTests.cs)
  and the existing
  [`tests/Headless.Messaging.Core.Tests.Unit/SubscribeExecutorRetryTests.cs`](../../tests/Headless.Messaging.Core.Tests.Unit/SubscribeExecutorRetryTests.cs).

**Test scenarios:**
- **Happy path:** `RetryBackoffOptions` with defaults validates clean.
- **Happy path:** Transient exception on attempt 1+2, success on attempt 3 → consumer
  observes `Headers.Attempt = 3`; the message is acked exactly once; no entry lands in the
  durable retry table.
- **Happy path:** `MessagingOptions.OutboxRetryBackoffStrategy` change does **not** affect
  in-flight retry behavior; `RetryBackoffOptions.MaxDelay` change does **not** affect storage
  redelivery cadence. Locks down the two-surface separation.
- **Edge case:** Strategy returns `ShouldRetry = false` → message routed to durable retry
  table on attempt 1; in-memory retry budget unspent.
- **Edge case:** Strategy returns `ShouldRetry = true` but `GetNextDelay = null` → routed to
  durable retry table.
- **Edge case (poison message):** A `JsonException` listed in
  `RetryBackoffOptions.NonRetryableExceptionTypes` → routed to durable retry table on attempt 1.
  Asserts that consumer apps wire serialization errors into the non-retryable set so poison
  messages do not consume `MaxAttempts` worth of CPU.
- **Edge case (reserved-header guard):** A publisher who supplies
  `headers["headless-attempt"] = "0"` in `PublishOptions.Headers` is rejected by
  `_ValidateCustomHeaders` with the same reserved-header exception shape as today's
  `Headers.MessageId` / `Headers.CorrelationId` guard.
- **Edge case:** `MaxAttempts = 1` (operator opts out of in-memory retry) → first failure
  routes immediately to durable retry table.
- **Edge case (per-instance attempt counter):** A failed consumer process restarts mid-retry;
  the peer instance reads the inbound `Headers.Attempt` value and starts incrementing from
  there. Asserts the documented per-instance semantics.
- **Edge case (no durable storage configured):** `RetryBackoffOptions.MaxAttempts` exhausted
  on a host where `MessageNeedToRetryProcessor` is not enabled (no SQL outbox / no Redis
  storage configured) → message logged at warning with `MessageId`, `Type`, and
  `lastException`, then dropped. Asserts the fall-through path: in-memory budget exhausted +
  no durable storage = log + drop, never silent retry-loop or unhandled exception. U6
  documents this as the "in-memory only" deployment posture.
- **Error path:** Strategy itself throws → swallow, log warning, route to durable retry
  table. The strategy exception does not crash the executor.
- **Error path:** `OperationCanceledException` matching the host-shutdown token → rethrow
  without consulting `ShouldRetry`; shutdown is not a retryable failure.
- **Error path:** Validator rejects `MaxAttempts = 0`, `BaseDelay = 0`, `MaxDelay < BaseDelay`,
  `JitterRatio > 1`, `JitterRatio < 0` with named property in the error message.
- **Integration:** Long in-process retry (`MaxDelay = 30s`) on Kafka does **not** cause a
  consumer-group rebalance — `SubscribeExecutor` releases the partition (commit + `Pause`)
  when the accumulated delay approaches `session.timeout.ms`. The Kafka provider integration
  test asserts no rebalance event during a 35s retry window. (Provider-specific behavior
  documented in U6 alongside the retry contract.)

**Verification:**
- `dotnet build --no-incremental -v:q -nologo /clp:ErrorsOnly` clean for affected projects.
- `dotnet test` green for `Headless.Messaging.Core.Tests.Unit` and the existing per-provider
  test suites (no per-provider integration changes are introduced by U4 itself).
- The new public surface (`RetryBackoffOptions`, `Headers.Attempt`) carries XML docs that
  compile under `TreatWarningsAsErrors`.
- `grep -r "RetryBackoffStrategy\b\|FailedRetryCount\b\|FailedRetryInterval\b" src/ tests/`
  returns zero matches for the unprefixed names — the rename is complete.

---

- U5. **Add `IActivityTagEnricher` to `Headless.Messaging.OpenTelemetry`**

**Goal:** Let consumer apps attach custom OpenTelemetry tags to publish/consume spans without
forking the package. Default implementation emits `headless.messaging.tenant_id`,
`headless.messaging.delivery_kind`, and the OTel-standardized `messaging.system` /
`messaging.operation.type` / `messaging.destination.name`.

**Requirements:** R6 (extensibility hook), R4 (tenancy visibility in traces).

**Dependencies:** None. U2 already shipped the typed `TenantId`.

**Files:**
- Create: `src/Headless.Messaging.OpenTelemetry/IActivityTagEnricher.cs`
- Create: `src/Headless.Messaging.OpenTelemetry/MessageTagContext.cs` — sealed record, no
  interface (single concrete consumer in Phase 1; defer interface extraction).
- Create: `src/Headless.Messaging.OpenTelemetry/DefaultActivityTagEnricher.cs`
- Create: `src/Headless.Messaging.OpenTelemetry/KnownFrameworkExceptionRedactor.cs` — internal
  helper invoked from `DiagnosticListener` before `Activity.AddException` for known framework
  exception types (gated by `SuppressTenantIdTag`).
- Create: `src/Headless.Messaging.OpenTelemetry/OpenTelemetryMessagingOptions.cs`
- Create: `src/Headless.Messaging.OpenTelemetry/OpenTelemetryMessagingOptionsValidator.cs`
- Modify: `src/Headless.Messaging.OpenTelemetry/Setup.cs` — add
  `AddActivityTagEnricher<T>()` extension and bind `OpenTelemetryMessagingOptions` via
  `AddOptions<,>().ValidateOnStart()`. The `AddInstrumentation` factory captures the
  `IServiceProvider` from the OTel `TracerProviderBuilder` registration scope so
  `DiagnosticListener` can resolve `IEnumerable<IActivityTagEnricher>` lazily on first event
  (the listener is constructed inside an `AddInstrumentation(() => ...)` factory closure today
  per `Setup.cs` lines 27-31; the closure captures the builder's services).
- Modify: `src/Headless.Messaging.OpenTelemetry/DiagnosticListener.cs` — invoke registered
  enrichers after stamping core tags.
- Modify: `src/Headless.Messaging.OpenTelemetry/MessagingInstrumentation.cs` — accept and
  forward the captured service provider to the listener.
- Test: `tests/Headless.Messaging.OpenTelemetry.Tests.Unit/ActivityTagEnricherTests.cs`
  (new test project — does not exist today; mirror the layout of
  `tests/Headless.Messaging.Core.Tests.Unit/` and add the project to the solution).

**Approach:**
- `IActivityTagEnricher.Enrich(Activity, MessageTagContext)` — context is a sealed record
  exposing typed `TenantId`, `DeliveryKind` (Phase 1: always `"send"`), `MessageType`,
  `Topic`, an `IReadOnlyDictionary<string, string?>` view of `Headers` (read-only;
  enrichers must not mutate), and `Provider` (the registered transport name).
- `DefaultActivityTagEnricher` emits:
  - `headless.messaging.tenant_id` when `TenantId` is non-null and
    `OpenTelemetryMessagingOptions.SuppressTenantIdTag` is false; **does not** emit an empty
    string when null (skip the tag entirely).
  - `headless.messaging.delivery_kind = "send"` (forward-compat anchor for Phase 2).
  - `messaging.system` per provider using OTel-registered enum values: `"kafka"`, `"rabbitmq"`,
    `"pulsar"`, `"servicebus"`, `"aws_sqs"`. NATS uses the not-yet-registered `"nats"`.
    InMemoryStorage / InMemoryQueue / PostgreSql / SqlServer **omit** the attribute.
  - `messaging.operation.type = "send"` for publish spans, `"process"` for consume spans.
  - `messaging.destination.name` from the resolved topic / subject / stream / queue.
- Multiple enrichers compose via DI registration order. `AddMessagingInstrumentation`
  registers `DefaultActivityTagEnricher` first; consumer apps call
  `AddActivityTagEnricher<TCustom>()` afterwards and run after the default. Replacing the
  default requires removing the default registration explicitly.
- `OpenTelemetryMessagingOptions` carries `SuppressTenantIdTag` only (default `false`). No
  `EnricherOrder` list — registration order matches the existing `IConsumerLifecycle` pattern.
- An enricher that throws is caught, logged at warning via `ILogger<DefaultActivityTagEnricher>`,
  and does not propagate. The span is still emitted with whatever tags previous enrichers
  managed to stamp.
- **Default enricher safety surface.** `DefaultActivityTagEnricher` emits **only** typed
  envelope fields (`TenantId`, `DeliveryKind`, `MessageType`, `Topic`, `Provider`) — it does not
  iterate over `Headers` and does not stamp exception messages. Consumer apps writing custom
  enrichers must scrub PII / secrets before stamping; the U5 README documents this constraint
  and points at the Phase 2 `DeadLetterEventScrubOptions` (U8) for an in-tree scrubbing
  pattern. No always-on header denylist ships in Phase 1 — adding one would silently change
  the documented "headers are passthrough" contract.
- **Known-framework-exception sanitization.** The existing `DiagnosticListener` calls
  `Activity.AddException(exception)` to record consume-side failures. By default,
  `AddException` records the full `exception.message` and `exception.stacktrace` as span
  attributes — and Headless framework exceptions (`MissingTenantContextException` from U10,
  the U2 `InvalidOperationException` for `TenantIdMismatch` / `ReservedTenantHeader`) carry
  tenant identifiers in their message text and `Data` dictionary. Without intervention,
  `SuppressTenantIdTag = true` would still leak tenant data through `exception.message`.
  U5 introduces a small `KnownFrameworkExceptionRedactor` invoked before every `AddException`
  call: when the exception is a known Headless framework type (matched by exact type +
  `Data["Headless.Messaging.FailureCode"]` value), the redactor stamps a synthetic
  `exception.type` + `exception.message` (with the tenant-bearing fields replaced by
  `[redacted]`) and `exception.stacktrace` directly via `activity.SetTag(...)`, then calls
  `activity.SetStatus(ActivityStatusCode.Error, redactedDescription)` to preserve the
  OTel-standard error-status semantics that `AddException` would have set automatically —
  bypassing `AddException`'s default serialization of `Data`. Without the explicit
  `SetStatus`, error-rate dashboards that count `status.code = ERROR` would silently
  under-count redacted publishes. Unknown exception types continue to use `AddException`
  unchanged (which sets the error status as part of its standard behavior). The redactor is gated by `SuppressTenantIdTag`: when `false`,
  `AddException` is called as today (verbatim), preserving debugging fidelity in
  single-tenant trace backends. Tested in `ActivityTagEnricherTests` with
  `MissingTenantContextException` round-trips under both flag values.

**Patterns to follow:**
- `IConsumerLifecycle` multi-registration pattern in `Headless.Messaging.Abstractions` for the
  shape of the enricher list.
- `OpenTelemetryMessagingOptionsValidator : AbstractValidator<OpenTelemetryMessagingOptions>` in
  the same file as the options class per `CLAUDE.md`.
- The existing `MessagingMetrics` source-generated metrics emission pattern for any new metrics
  (none are added in U5; flagged for U6 documentation only).

**Test scenarios:**
- **Happy path:** Default enricher adds `headless.messaging.tenant_id = "acme"` when `TenantId`
  is set on the envelope.
- **Happy path:** Default enricher omits `headless.messaging.tenant_id` when `TenantId` is null
  (asserts the tag is not present, not just that it equals empty string).
- **Happy path:** Default enricher emits `messaging.system = "rabbitmq"` for the RabbitMq
  provider; emits no `messaging.system` for InMemoryStorage.
- **Happy path:** A user-supplied `IActivityTagEnricher` registered after `Default` runs after
  it and observes the same `MessageTagContext`.
- **Happy path:** A user-supplied enricher registered **before** `Default` (via
  `services.Insert(0, ...)` or by registering before `AddMessagingInstrumentation`) runs
  first — DI registration order is the contract.
- **Happy path:** `headless.messaging.delivery_kind = "send"` is present on every publish span.
- **Edge case:** `SuppressTenantIdTag = true` → the tag is omitted even when `TenantId` is set.
- **Edge case (exception redaction with flag on):** `MissingTenantContextException` thrown
  during a consume call with `SuppressTenantIdTag = true` produces a span where
  `exception.message` does not contain the tenant identifier from the exception's `Data`
  dictionary; `exception.type` is preserved verbatim. Asserts that `KnownFrameworkExceptionRedactor`
  intercepts known framework exceptions before `AddException` runs.
- **Edge case (exception verbatim with flag off):** Same exception thrown with
  `SuppressTenantIdTag = false` produces a span where `exception.message` matches the original
  `Exception.Message` text — debugging fidelity is preserved in single-tenant backends.
- **Edge case (error-status preserved on redaction):** `MissingTenantContextException` with
  `SuppressTenantIdTag = true` produces a span where `Activity.Status == ActivityStatusCode.Error`.
  Asserts the redactor calls `SetStatus(Error)` to compensate for skipping `AddException`.
  Without this, error-rate dashboards counting `status.code = ERROR` would under-count
  redacted publishes.
- **Edge case (unknown exception):** A consumer-domain `KeyNotFoundException` thrown during
  consume is stamped via the standard `AddException` path regardless of the flag value — the
  redactor is not a generic exception scrubber.
- **Edge case:** Enricher throws `InvalidOperationException` → span is still emitted, exception
  logged at warning, subsequent enrichers run.
- **Edge case:** Enricher pool is empty (all enrichers removed) → span emitted with only the
  core OTel-stamped tags; no exception.
- **Integration:** A publish→consume round-trip yields two connected spans both carrying
  `headless.messaging.tenant_id = "acme"`, with the publish span tagged
  `messaging.operation.type = "send"` and the consume span tagged `"process"`.
- **Integration:** Trace context (W3C `traceparent`) propagation continues to work — the
  enricher hook does not regress existing behavior.

**Verification:**
- `dotnet test` green for the new `Headless.Messaging.OpenTelemetry.Tests.Unit` project.
- An OpenTelemetry test harness (an in-memory `ActivityListener`) confirms tag presence,
  absence (for null TenantId), and order.
- `Headless.Messaging.OpenTelemetry/README.md` documents the enricher hook with an example.

---

- U10. **Strict-tenancy publish guard (#238)**

**Goal:** When `MessagingOptions.TenantContextRequired = true`, publishing without a resolved
`TenantId` fails fast at the publish wrapper with `MissingTenantContextException`. Mirrors the
EF write guard (#234) and Mediator behavior (#236) so the cross-layer tenancy stack has a
consistent failure shape.

**Requirements:** R4 (extension), parity with #234 / #236.

**Dependencies:** U2 (shipped). Independent of U4 / U5 / U6 — can land in any order relative to
those.

**Files:**
- Create: `src/Headless.Messaging.Abstractions/Exceptions/MissingTenantContextException.cs`
- Modify: `src/Headless.Messaging.Core/Configuration/MessagingOptions.cs` — add
  `bool TenantContextRequired { get; set; } = false`.
- Modify: `src/Headless.Messaging.Core/Configuration/MessagingOptionsValidator.cs` (extend if
  exists, otherwise create same-file validator) — when `TenantContextRequired = true`, assert
  `ICurrentTenant` is registered as something other than `NullCurrentTenant` (cross-package
  contract — fail at startup, not at first publish).
- Modify: `src/Headless.Messaging.Core/Internal/IMessagePublishRequestFactory.cs` — extend the
  factory's primary constructor to accept `ICurrentTenant`; extend `_ApplyTenantId` with the
  ambient-tenant resolution step described under Key Technical Decisions (8) and (9). The
  factory remains **singleton** (current registration in `Setup.cs:96`); `ICurrentTenant`
  remains **singleton** with the existing `AsyncLocalCurrentTenantAccessor`
  (`Headless.Api/Setup.cs:166`). The AsyncLocal accessor reads per-call from the publish
  scope's flow context — no captive-dependency bug because the singleton holds an
  AsyncLocal-backed accessor, not a scoped value. **No factory lifetime change required.**
- **Background-worker contract.** When a publish originates from a background `IHostedService`
  (recurring job, scheduled task, outbox drainer) with no ambient HTTP/MQ request scope,
  `ICurrentTenant.Id` is `null` by default. Operators with `TenantContextRequired = true` must
  set the tenant explicitly before publish — either by wrapping the publish call in
  `using (currentTenant.Change(tenantId))` (scopes the AsyncLocal accessor for the
  background context) or by populating `PublishOptions.TenantId` directly on the call. The
  U10 guard's failure message names `ICurrentTenant.Change(...)` as the remediation; U6
  includes a worked example for background-job authors and a callout referencing the
  outbox-drainer pattern.
- Test: `tests/Headless.Messaging.Core.Tests.Unit/Internal/StrictTenancyPublishGuardTests.cs`
- Test: `tests/Headless.Messaging.Core.Tests.Unit/Configuration/MessagingOptionsValidatorTenancyTests.cs`

**Approach:**
- The new option `TenantContextRequired` defaults to `false` to preserve today's behavior.
  Setting it to `true` is the explicit opt-in.
- Inside `_ApplyTenantId`, after the existing 4-case integrity check resolves
  `typed = options?.TenantId`, the guard branch runs:
  - If `TenantContextRequired = false` — no change from today.
  - If `TenantContextRequired = true` and `typed is null`:
    - Resolve `ICurrentTenant.Id` from DI.
    - If the resolved value is non-null, set `typed = resolved` and re-enter the U2 stamping
      flow (so the four-case integrity check stays the single point of truth for header/typed
      reconciliation).
    - If both are null, throw `MissingTenantContextException` with
      `Data["Headless.Messaging.FailureCode"] = "MissingTenantContext"`.
- `MissingTenantContextException : InvalidOperationException` (preserves catch-compat with the
  existing publish-failure exception family). Lives in
  `Headless.Messaging.Abstractions/Exceptions/` so consumer apps can `catch (MissingTenantContextException)`
  without a `Core` dependency.
- `MessagingOptionsValidator` cross-checks: when `TenantContextRequired = true` is set,
  `ICurrentTenant` must be registered as a non-`NullCurrentTenant` implementation. This is
  detected by resolving `ICurrentTenant` once at startup and failing if the concrete type is
  `NullCurrentTenant`. Failure message: "MessagingOptions.TenantContextRequired = true but
  ICurrentTenant is not registered. Add Headless.Core (or a custom ICurrentTenant) to the DI
  container before calling AddMessaging."
- The guard runs **after** the existing 4-case header check from U2. If both a header injection
  AND a missing tenant context exist on the same publish, the U2 check fires first
  (failure code `"ReservedTenantHeader"`). The U10 check is a separate failure path with code
  `"MissingTenantContext"`.

**Patterns to follow:**
- `MessagePublishRequestFactory._ApplyTenantId` (U2-shipped) for the validation shape and the
  `Data["Headless.Messaging.FailureCode"]` key naming.
- The cross-layer guard pattern from #234 (EF write guard) and #236 (Mediator behavior) —
  the failure code and exception type align with those siblings so log aggregators can group
  by `Headless.*.FailureCode`.

**Test scenarios:**
- **Happy path:** `TenantContextRequired = false`, no tenant set → publish succeeds with
  `TenantId = null` (preserves today's behavior).
- **Happy path:** `TenantContextRequired = true`, `PublishOptions.TenantId = "acme"` → publish
  succeeds; ambient tenant is **not** consulted.
- **Happy path:** `TenantContextRequired = true`, `PublishOptions.TenantId` null,
  `ICurrentTenant.Id = "acme"` → publish succeeds with `TenantId = "acme"` stamped onto headers.
- **Happy path:** `TenantContextRequired = true`, `PublishOptions.TenantId = "acme"`,
  `ICurrentTenant.Id = "beta"` → publish succeeds with `TenantId = "acme"` (explicit publish-side
  value wins; ambient is a fallback only).
- **Edge case:** `TenantContextRequired = true`, both null → throws
  `MissingTenantContextException` with `FailureCode = "MissingTenantContext"` and a remediation
  message naming `ICurrentTenant.Change(...)`.
- **Edge case (background worker happy path):** `TenantContextRequired = true`, publish runs
  inside `using (currentTenant.Change("acme"))` from an `IHostedService` (no ambient HTTP
  scope) → publish succeeds with `TenantId = "acme"` resolved from the AsyncLocal scope set
  by `Change`. Asserts the documented background-worker remediation actually works.
- **Edge case (background worker no-tenant):** `TenantContextRequired = true`, publish runs
  in an `IHostedService` without `Change` and without `PublishOptions.TenantId` →
  `MissingTenantContextException`. Same failure as the HTTP path.
- **Edge case:** `TenantContextRequired = true` but `ICurrentTenant` is `NullCurrentTenant` →
  startup fails with `OptionsValidationException` (caught by `ValidateOnStart`).
- **Edge case:** `TenantContextRequired = true`, raw header set without typed property → U2's
  `ReservedTenantHeader` check fires first; U10 does not run on this path.
- **Edge case:** `TenantContextRequired = true`, raw header and typed property disagree → U2's
  `TenantIdMismatch` check fires first.
- **Error path:** `MissingTenantContextException` thrown from the publish wrapper does **not**
  emit an envelope to any transport — assertion via a fake provider that no `OnPublish` call
  was observed.
- **Integration:** The exception type is catchable as `InvalidOperationException` (base class
  parity for log aggregators that group on the base exception).

**Verification:**
- `dotnet test` green for `Headless.Messaging.Core.Tests.Unit`.
- A targeted integration test in
  `tests/Headless.Messaging.InMemoryStorage.Tests.Integration/StrictTenancyTests.cs` (new)
  exercises the guard end-to-end against a real transport.
- `grep -r "MissingTenantContextException" src/ tests/` finds the type in exactly one source
  file and the expected test references; the failure code string `"MissingTenantContext"`
  matches the documented contract in U6.

---

- U6. **Capability matrix doc + `docs/llms/messaging-envelope.md`**

**Goal:** Single source of truth for the envelope shape, publisher capability per transport, the
cross-provider retry contract, the OTel attribute table, the strict-tenancy guard semantics, and
operational runbooks for convention-axis transports. Referenced by every provider README and the
abstractions README.

**Requirements:** R9.

**Dependencies:** U4 (retry contract definitions), U5 (OTel attribute table), U10 (strict-
tenancy failure-code list). Lands last in the plan so the matrix reflects shipped reality, not
planned reality.

**Files:**
- Create: `docs/llms/messaging-envelope.md`
- Modify: `docs/llms/messaging.md` — add a "see also" link to the envelope doc.
- Modify: `src/Headless.Messaging.Abstractions/README.md` — add a capability summary and a link.
- Modify: each `src/Headless.Messaging.<Provider>/README.md` — state which publisher interfaces
  the provider supports (Phase 1 reality: `IDirectPublisher` for all; `IOutboxPublisher` where
  the provider has a transactional store; explicit "broadcast support arrives in Phase 2"
  callout for capability-axis providers).
- Modify: top-level `README.md` and any solution-level docs that reference messaging.

**Approach:**
- Doc sections (in order):
  1. **Envelope reference** — typed properties (`PublishOptions`, `ConsumeContext`), wire
     headers (`Headers.*` constants including `Attempt` from U4 and `TenantId` from U2),
     validation rules (200-char limits, charset delegation to consumers).
  2. **Capability matrix** — table from #217 §"Transport Capability Matrix" with a Phase 1
     status column (all providers ship `IDirectPublisher` today; broadcast-axis is "Phase 2").
     Includes fan-out mechanism column and operational-cost column.
  3. **Retry contract** — verbatim copy of the contract definitions from U4 with worked
     examples per provider. Documents the `Headers.Attempt` canonical counter, the
     immediate-vs-requeue threshold, the DLQ fall-through pathway, the publisher-does-not-stamp
     rule, and the cancellation-vs-timeout `OperationCanceledException` distinction. Includes
     the U4 audit notes per provider, the per-provider lock/lease vs `MaxDelay` constraints
     (NATS `ack_wait`, ASB lock duration, Pulsar `ackTimeout`, SQS visibility, Kafka
     `session.timeout.ms`), the recommended `NonRetryableExceptionTypes` set (`JsonException`,
     `SerializationException`, well-known consumer-domain validation exceptions), and a
     **DLQ replay recipe** per provider for stripping `headless-attempt` before re-injection.
  4. **OTel attribute table** — every attribute the package emits (`headless.messaging.*` plus
     OTel-standardized `messaging.*`), with cardinality notes and a "what to alert on" tip.
     Includes `SuppressTenantIdTag` operational guidance for cross-tenant trace storage.
  5. **Strict-tenancy guard** — `TenantContextRequired` semantics, the four failure codes
     (`ReservedTenantHeader`, `TenantIdMismatch`, `MissingTenantContext`, plus the existing
     header-validation failures), a worked example showing the U2 + U10 interaction.
  6. **Convention-axis operational runbook** — for transports where Send vs Broadcast is a
     subscriber-side configuration choice (Kafka, NATS, RedisStreams, Pulsar), document how
     operators verify at runtime which axis a deployed consumer is wired to. Phase 1 entry is
     scoped to "today everything is Send"; the broadcast section is a stub flagged "Phase 2" so
     the doc URL is stable across phases.
  7. **Migration notes for current consumer apps** — `grep`/`sed` recipes for adopting the new
     `RetryBackoffOptions` config section, the OTel enricher hook, and the
     `TenantContextRequired` opt-in. (Phase 2 rename recipes are added in the Phase 2 plan,
     not stubbed here.)
  8. **Security posture cross-link** — single-sentence pointer to
     [`specs/2026-04-19-001-messaging-feature-spec.md`](../../specs/2026-04-19-001-messaging-feature-spec.md)
     §Security Considerations rather than duplicating the threat table (avoids divergence when
     the spec is updated).
- Each provider README gains a one-paragraph "Capability statement" with a link to the matrix
  doc.

**Test scenarios:**
- Test expectation: none — documentation unit. Reviewable by reading the rendered doc.

**Verification:**
- Manual review: a reader can pick the right provider and configure `RetryBackoffOptions` /
  `TenantContextRequired` from the doc alone.
- `grep -rn "see also" docs/llms/messaging.md` includes the envelope doc link.
- `grep -rn "capability" src/Headless.Messaging.*/README.md` finds a statement in every
  provider README.
- `grep -rn "Phase 2" docs/llms/messaging-envelope.md` produces the expected stubs at:
  Send/Broadcast split, broadcast capability column, NATS-ergonomics features.

---

## System-Wide Impact

- **Interaction graph:** U4 extends the existing central retry pipeline (`SubscribeExecutor`)
  to read calibrated `RetryBackoffOptions`; the publish-side path
  (`MessagePublishRequestFactory`) is unchanged by U4. Providers are unchanged. U10 extends
  the publish path with an ambient-tenant resolution step. U5 modifies the OTel
  `DiagnosticListener` to invoke registered enrichers between core-tag stamping and span
  emission, and gates known-framework-exception detail through `KnownFrameworkExceptionRedactor`
  before `Activity.AddException`.
- **Error propagation:** `IRetryBackoffStrategy.ShouldRetry` continues to be the single
  decision point for retry-vs-DLQ across providers (already shipped at
  `SubscribeExecutor.cs:208`). Strategy exceptions are caught and logged; the executor
  routes to the durable retry table rather than crashing. `MissingTenantContextException`
  is a new publish-time failure mode that propagates to the caller exactly like the existing
  U2 4-case integrity violations; downstream callers that already catch
  `InvalidOperationException` from `_ApplyTenantId` continue to work without code changes.
- **State lifecycle risks:** `Headers.Attempt` is a per-instance counter held by
  `SubscribeExecutor` keyed by `MessageId`. After lease expiry on a peer instance the counter
  restarts from the inbound wire value — documented as accepted residual risk. The OTel
  enricher reads the raw header dictionary; an enricher that mutates headers is documented
  as undefined behavior in U6.
- **API surface parity:** U4 adds `Headers.Attempt` constant and `RetryBackoffOptions`
  to abstractions / Core; renames `MessagingOptions.RetryBackoffStrategy` →
  `OutboxRetryBackoffStrategy`, `FailedRetryCount` → `OutboxFailedRetryCount`,
  `FailedRetryInterval` → `OutboxFailedRetryInterval` (greenfield breaking change per
  `CLAUDE.md`). U5 adds `IActivityTagEnricher`, `MessageTagContext` (sealed record), and
  `OpenTelemetryMessagingOptions` to the OTel package; U10 adds `MissingTenantContextException`
  and `MessagingOptions.TenantContextRequired`. The U4 rename is the only breaking change
  in Phase 1. Phase 2 will remove `IDirectPublisher` / `IOutboxPublisher` as a separate
  breaking change.
- **Integration coverage:** Every provider gains a `RetryBackoffTests.cs` integration test
  (extending the existing per-provider integration suites) that exercises both `ShouldRetry` true/false branches and the
  `MaxAttempts` boundary. The OTel enricher hook is exercised by an in-memory `ActivityListener`
  harness in `Headless.Messaging.OpenTelemetry.Tests.Unit`. The strict-tenancy guard is
  exercised in `InMemoryStorage.Tests.Integration` end-to-end.
- **Unchanged invariants:** `IConsume<T>`, `ConsumeContext<T>`, `PublishOptions`,
  `IDirectPublisher`, `IOutboxPublisher`, `IScheduledPublisher`, `MessagingConventions`, the
  W3C trace-context propagation in `DiagnosticListener`, and every provider's transport-native
  acknowledgement primitives. `MessageNeedToRetryProcessor` (failed-message storage retry) is
  untouched in behavior and continues to read the renamed
  `MessagingOptions.OutboxRetryBackoffStrategy`.

## Risks & Dependencies

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Per-provider audit reveals existing transport-level behavior (e.g., RabbitMQ DLX with TTL silently re-injects messages with a stale `Headers.Attempt`) that interacts unexpectedly with the central retry pipeline | Med | Med | U6 audit notes document each transport's native primitives (Ack/Nack/visibility/lease) and how they interact with the in-process retry counter. PRs that change observable retry behavior are flagged in commit messages and release notes. |
| `RetryBackoffOptions` defaults differ from a provider's prior implicit defaults, surprising production consumers on first deploy | Med | Med | Document defaults in U6 release-note section; the only real difference vs today is **MaxAttempts = 5** (unbounded in some providers today) — call it out explicitly. |
| `IActivityTagEnricher` hook is invoked in a hot path; a slow enricher adds publish/consume latency | Med | Med | Document a "do work fast / no I/O" contract in U5; assertion in U5 tests that an enricher returning in <1ms keeps publish span overhead within 5% of baseline. |
| Custom enricher stamps exception messages or PII-bearing headers onto span tags, leaking sensitive data to the trace backend | Med | Med | U5 README + U6 callout: enrichers run on raw headers; consumer apps are responsible for scrubbing PII before stamping. The framework ships a built-in scrubber in Phase 2 (U8 / `DeadLetterEventScrubOptions`); Phase 1 is the documented warning. The `DefaultActivityTagEnricher` does **not** stamp exception messages and does not iterate over `Headers` — it only emits typed envelope fields (`TenantId`, `DeliveryKind`, etc.). |
| Long-delay retry on Kafka triggers consumer-group rebalance because heartbeats stop while in `Task.Delay` | Med | High | `SubscribeExecutor` heartbeat-safe delay logic: when accumulated delay approaches `session.timeout.ms`, the central pipeline commits + `Pause`s the partition and rejoins on resume. Documented in U6 as a Kafka-specific operator note. The `ImmediateRetryThreshold` default (250ms) keeps the common case heartbeat-safe. |
| `RetryBackoffOptions.MaxDelay` exceeds the broker's lock/lease timeout (NATS `ack_wait`, ASB lock duration, Pulsar `ackTimeout`, SQS visibility) → broker redelivers to peer mid-retry, double-consume | Med | High | **ASB** (the only provider with a typed `SubscriptionMessageLockDuration` field today) ships a startup validator asserting `LockDuration > MaxDelay + ImmediateRetryThreshold + 5s`. NATS `AckWait` is hard-coded inside `NatsConsumerClient` and Pulsar/SQS/Kafka have no typed timeout fields — for those, U6 documents the constraint as **operator responsibility** with a worked example showing how to compute the safe `MaxDelay` from each broker's effective lock timeout. Phase 2 expands the validator surface as typed timeout options are added per provider. |
| Operator manually replays a DLQ message with a stale `headless-attempt` header → re-DLQ on first attempt | Med | Low | U6 operator runbook documents per-provider replay recipes that strip `headless-attempt` before re-injection (RabbitMQ shovel, ASB resubmit, SQS redrive). The framework does not auto-reset because legitimate replay vs buggy duplicate-publish are indistinguishable from the consumer's view. |
| `OpenTelemetryMessagingOptions.SuppressTenantIdTag` is missed in audit; tenant identity leaks to a shared trace backend | Low | High | U6 surfaces the option in a "Cross-tenant trace storage" callout next to the security posture summary; the OTel test harness asserts the suppression behavior. |
| `MissingTenantContextException` masks a deeper auth/identity bug in a host where `TenantContextRequired = true` is enabled defensively | Low | Med | The exception message names `ICurrentTenant.Change(...)` as the remediation — debugger-friendly stack with the specific DI scope where the resolution failed. |
| New `Headless.Messaging.OpenTelemetry.Tests.Unit` project drifts out of CI | Low | Low | Adding the project to the solution is a verification step in U5; the Phase 1 PR cannot land without the test run passing. |
| Per-message-type `IRetryBackoffStrategy<TMessage>` registrations interact unexpectedly with consumer-app DI lifetimes (e.g., scoped strategies resolved from the singleton dispatcher) | Med | Med | The dispatcher resolves the strategy per-call (not per-message-loop) and uses the ambient scope; documented in U6 with a "do not inject scoped state into custom strategies" callout. Validator catches the common misregistration cases at startup. |
| Transport-native redelivery (RabbitMQ DLX with TTL, NATS Nak after lease expiry) re-injects with a stale `Headers.Attempt` carried on the wire | Med | Med | Per-instance attempt counter contract (see Cross-Provider Retry Contract Definitions): the in-process counter held by `SubscribeExecutor` is authoritative within an instance; after lease expiry the peer instance restarts from the inbound wire value. Documented per-provider in U6 with worked examples. |

## Security Considerations

This plan does not change the threat model articulated in #217 §"Security Considerations". The
relevant deltas are:

1. **Strict-tenancy publish guard (U10) closes one of the four threats listed in the canonical
   spec.** Threat 1 ("Header injection / publisher trust boundary") was closed by U2 / #228.
   U10 closes the "publish without ambient tenant when strict tenancy is required" pathway.
2. **OTel tenant-tag suppression (U5).** `SuppressTenantIdTag` exists for environments where
   tenant identity is itself sensitive. The default is `false` to preserve observability; the
   option is documented in U6 alongside the data-residency / PII-classification operator
   guidance.
3. **DLQ secret leakage** (Threat 2) is **not** addressed by this plan. The
   `IDeadLetterObserver` surface and `DeadLetterEventScrubOptions` opt-in scrubber are part of
   U8 (NATS-ergonomics phase). Phase 1 documentation in U6 reiterates the "do not put secrets
   in headers" guidance.
4. **Tenant impersonation via dedup collision** (Threat 3) is closed once the composite
   `(TenantId, MessageId)` dedup keys ship in Phase 2 (U3a/U3b). Phase 1 makes `TenantId`
   envelope-visible so existing dedup paths can opt in opportunistically; the per-provider
   migration is Phase 2.

## Accepted Residual Risks

These risks are not fully mitigated in Phase 1. The plan accepts them with documented operator
guidance rather than expanding scope to close them.

- **Kafka commits-before-retry on `pause()`/`seek()` may surface as message loss if the
  consumer process dies mid-retry.** The dispatcher commits the offset before the in-process
  retry to avoid rebalance, then re-reads on resume. If the host dies in the retry window, the
  message is lost. Accepted because: (a) Kafka has no native delayed-redelivery primitive,
  (b) consumer apps requiring durability across process death use the outbox decorator (Phase
  2). U6 documents the trade-off explicitly.
- **`Headers.Attempt` is per-instance, not per-message-globally, when a peer instance picks up
  after lease expiry.** RabbitMQ `BasicNack(requeue)`, NATS `Nak`, Pulsar negative-ack, SQS
  `ChangeMessageVisibility`, and Redis Streams `XCLAIM` redeliver the original message
  payload — the framework cannot mutate the wire header on these paths without a full
  re-publish (which would change `MessageId` and break dedup). The peer therefore observes the
  pre-failure value of `Headers.Attempt`. This is provider-defined behavior; U6 documents the
  per-provider answer. Accepting it preserves dedup and at-least-once semantics; the cost is
  that retry-budget exhaustion across instances requires the strategy to be tolerant of
  "attempt counter resets at instance boundary."
- **Outbox drainer publishes from a background `IHostedService` with no ambient request scope.**
  When `TenantContextRequired = true`, an outbox redispatch must already carry `TenantId` on
  the persisted envelope (set at the original publish-time scope by the U10 guard). The
  drainer-side path does **not** consult `ICurrentTenant` — the tenant is read from the
  envelope. U6 documents this explicitly so operators don't expect the drainer to enforce
  tenant context against the drainer's empty execution scope.
- **`Activity.AddException` records exception detail (including `MissingTenantContextException`
  message text) regardless of `SuppressTenantIdTag`.** Phase 1 does not gate exception-message
  emission. Operators in cross-tenant trace-storage environments either (a) configure their
  trace exporter to redact `exception.*` attributes, or (b) consume the exception via a custom
  `IActivityTagEnricher` that strips before emission. The Phase 2 `DeadLetterEventScrubOptions`
  surface (U8) is the in-tree path for full exception scrubbing.

## Documentation / Operational Notes

- U6 lands in the same PR (or PR series) as U4/U5/U10. Each unit's XML docs are
  updated inline — U6 is the cross-cutting reference, not the only doc surface.
- A release-note section for the next minor version calls out:
  - **New:** `Headless:Messaging:RetryBackoff` configuration section, `IActivityTagEnricher`
    hook, `MessagingOptions.TenantContextRequired` opt-in.
  - **Behavior change:** every provider's consumer dispatch loop now honors
    `IRetryBackoffStrategy`; `Headers.Attempt` is the canonical attempt counter. Operators
    used to provider-specific retry semantics should review the U6 retry contract section.
  - **Deprecated:** none.
- No runtime rollout plan: this is a framework release, not a service deploy.

## Sequencing

```text
U4 (Calibrate central retry pipeline; rename Outbox* properties; SubscribeExecutor extension)

U5 (OTel enricher + known-framework-exception redactor)   # no sequential deps on U4

U10 (#238 strict-tenancy publish guard)                   # no sequential deps on U4/U5

U6 (capability matrix + retry contract + OTel attribute table + strict-tenancy doc + replay
    recipes + per-provider lock-vs-MaxDelay constraints)  # consumes shipped U4/U5/U10 reality

U6 (capability matrix doc)    # last — references shipped reality from U4/U5/U10
```

**Parallelism:** U4, U5, and U10 can ship in parallel (different files, no sequential
dependencies). U6 lands last and consumes the shipped reality from all three.

## Sources & References

- Origin (canonical spec): <https://github.com/xshaheen/headless-framework/issues/217>
- Spec document: [`specs/2026-04-19-001-messaging-feature-spec.md`](../../specs/2026-04-19-001-messaging-feature-spec.md)
- Child issues:
  - U4: <https://github.com/xshaheen/headless-framework/issues/229>
  - U5: <https://github.com/xshaheen/headless-framework/issues/230>
  - U6: <https://github.com/xshaheen/headless-framework/issues/231>
  - U10 / strict-tenancy guard: <https://github.com/xshaheen/headless-framework/issues/238>
- Sibling tenancy work: #234 (EF write guard), #236 (Mediator behavior), #237 (ProblemDetails handler)
- Predecessor plan (U2 envelope, completed): [`docs/plans/2026-05-01-001-feat-tenant-id-envelope-plan.md`](2026-05-01-001-feat-tenant-id-envelope-plan.md)
- MassTransit retry layering: <https://masstransit.io/documentation/configuration/retry>
- OpenTelemetry messaging semantic conventions: <https://opentelemetry.io/docs/specs/semconv/messaging/>
- `CLAUDE.md` (greenfield posture, FluentValidation + `ValidateOnStart` options pattern,
  `Headless.Checks` argument validation).
