---
title: Messaging Native OpenTelemetry Migration - Plan
type: refactor
date: 2026-07-15
topic: messaging-native-otel
artifact_contract: x-unified-plan/v1
artifact_readiness: requirements-only
product_contract_source: x-brainstorm
execution: code
---

# Messaging Native OpenTelemetry Migration - Plan

## Goal Capsule

- **Objective:** Migrate messaging telemetry from the `DiagnosticSource`→span bridge to native BCL `Activity`/`Meter` emission inside `Headless.Messaging.Core`, preserving the semconv instrument names, `headless.messaging.*` attributes, W3C context propagation, and the enricher API — then delete `Headless.Messaging.OpenTelemetry`. Breaking changes accepted (greenfield charter).
- **Product authority:** Repo owner (Mahmoud Shaheen) — approved 2026-07-15. Convention basis: [docs/solutions/conventions/opentelemetry-instrumentation-conventions.md](../solutions/conventions/opentelemetry-instrumentation-conventions.md).
- **Open blockers:** None. Sequencing: land after the caching OTel work ([2026-07-15-001](2026-07-15-001-feat-caching-otel-instrumentation-plan.md)) establishes the native reference; this plan must not gate #384.

## Product Contract

### Summary

Move span and metric emission from the three `DiagnosticSource` sites in `Headless.Messaging.Core` to direct `ActivitySource`/`Meter` calls, relocate the propagation logic and the `IActivityTagEnricher` pipeline from the bridge into Core (running enrichers synchronously at span start), and delete the `Headless.Messaging.OpenTelemetry` package. Consumers register via a typed `AddMessagingInstrumentation()` that reduces to `AddSource`/`AddMeter`, or subscribe by the public name const — identical to every other subsystem.

### Problem Frame

Messaging is the last subsystem on the pre-2021 `DiagnosticSource`→span bridge: Core writes payload events (`DirectPublisherCore.cs`, `ISubscribeExecutor.cs`, `IConsumerRegister.cs`) and a satellite package subscribes, translates them to `Activity` spans, runs enrichers, and does W3C propagation. The two features once thought to require the bridge are portable — the propagation code already uses `OpenTelemetry.Api` types that can live in Core now that implementation packages may reference `OpenTelemetry.Api` (80 KB, dependency-free on net10), and the enricher API needs only the `Activity` plus context. The bridge also carries a documented defect shape: async enrichers become fire-and-forget and their tags are silently dropped. Native emission removes the translation layer, the satellite package, and that wart in one pass.

### Key Decisions

- **K1. Native emission in Core, bridge deleted.** Spans and metrics are emitted directly at the current event sites; the `DiagnosticSource` write/`IsEnabled` plumbing and the entire `Headless.Messaging.OpenTelemetry` package are removed. No compatibility shim — breaking change, announced in the release notes.
- **K2. Telemetry names are preserved verbatim.** Instrument names and standard dimensions stay OTel messaging semconv (`messaging.publish.messages`, `messaging.consume.duration`, `messaging.operation`/`messaging.system`/`messaging.consumer.group`/`error.type`); custom attributes stay `headless.messaging.*`; the Meter/ActivitySource stays `Headless.Messaging`. Dashboards and alerts built on today's names survive the migration untouched.
- **K3. Enrichers move to Core and run synchronously at span start.** `IActivityTagEnricher`, the three built-ins (tenant-id, intent, retry-count), and the suppression toggles relocate to `Headless.Messaging.Core`; custom enrichers register on the messaging setup builder (matching caching's setup-builder-owned telemetry config), not at OTel-registration time. Synchronous invocation fixes the fire-and-forget async wart by construction.
- **K4. Propagation is always-on in Core.** `traceparent`/baggage injection on publish and extraction on consume move into the emission path via `OpenTelemetry.Api` propagation types, unconditional — a non-exporting service still forwards context so downstream trace continuity survives.
- **K5. The `EnableMetrics` toggle is dropped.** Native instruments are near-free when unobserved (`Counter.Enabled`/`HasListeners()` early-outs); subscribing to the meter is the toggle.

### Requirements

**Emission migration**

- R1. Publish, consume, and subscriber-invoke spans and metrics are emitted natively from `Headless.Messaging.Core` at the sites that currently write `DiagnosticSource` events; the `DiagnosticSource` plumbing is removed.
- R2. Every instrument name, span name, standard dimension, and `headless.messaging.*` attribute emitted today is emitted identically after the migration (K2), verified by a before/after telemetry-parity test using the InMemory exporter.
- R3. Emission adds no measurable overhead when no listener is attached (`HasListeners()`/`Counter.Enabled` early-outs, no allocation on the unobserved path).

**Propagation**

- R4. Trace context (`traceparent`) and baggage are injected into outgoing message headers on publish and extracted on consume, always-on (K4), with behavior parity to the current bridge across the async publish→outbox→transport→consume boundary.
- R5. A cross-boundary trace shows the consume-side span correctly related to the publish-side trace (parent or link — exact relation per Q1) for every transport.

**Enrichment**

- R6. `IActivityTagEnricher` and the built-in tenant-id/intent/retry-count enrichers live in `Headless.Messaging.Core`; suppression toggles and custom-enricher registration move to the messaging setup builder (K3).
- R7. Enrichers are invoked synchronously at span start; tags added by an enricher are always attached before the span can end (closes the fire-and-forget wart). Enricher exceptions remain isolated and logged without failing the messaging operation.
- R8. The enricher PII guardrails carry over: reserved namespaces (`messaging.*`, `server.*`, `headless.messaging.*`, `exception.*`) stay documented and tenant-id tagging remains suppressible.

**Registration and package removal**

- R9. `Headless.Messaging.Core` exposes the instrumentation name as a `public const string` and ships typed `AddMessagingInstrumentation()` extensions on `TracerProviderBuilder`/`MeterProviderBuilder` that reduce to `AddSource`/`AddMeter` — requiring only `OpenTelemetry.Api`, never the SDK, and never touching `Headless.Messaging.Abstractions`.
- R10. `Headless.Messaging.OpenTelemetry` is deleted from the solution, packaging, and docs; release notes state the consumer migration (`AddMessagingInstrumentation()` moves namespace; options move to the messaging setup builder).

**Docs**

- R11. `docs/llms/messaging.md` and the affected READMEs are updated in the same change: registration examples, enricher registration surface, the tag/toggle table, and removal of the satellite-package section.

## Acceptance Examples

- AE1. Covers R2. **Given** a publish and consume through any transport with the InMemory exporter attached, **when** telemetry is captured before and after the migration, **then** the sets of instrument names, span names, and attribute keys are identical.
- AE2. Covers R4, R5. **Given** service A publishes and service B consumes, **when** B starts its consume span, **then** B's span belongs to A's trace (via the Q1-chosen relation) with baggage intact.
- AE3. Covers R7. **Given** a custom enricher that adds a tag, **when** the messaging span ends immediately after start, **then** the tag is present on the exported span — including for enrichers that would previously have completed asynchronously.
- AE4. Covers R9, R10. **Given** a consumer app referencing only `Headless.Messaging.Core` and its own OTel SDK setup, **when** it calls `AddMessagingInstrumentation()` on both provider builders, **then** messaging traces and metrics flow with no reference to any deleted package.

## Scope Boundaries

- In scope: telemetry emission relocation, propagation relocation, enricher relocation, package deletion, docs.
- Not in scope: any behavior change to the messaging pipeline itself (outbox, retry, circuit breaker, dispatch); metric/attribute renames (K2 forbids); the caching OTel work (separate plan).
- Deferred: resolving the `headless.messaging.*.duration_ms` span-attribute smell (Q2) — may land here if cheap, but is not required for the migration to complete.

## Dependencies / Assumptions

- Sequenced after the caching plan establishes the native reference shape (name const + typed helper pattern); not blocked by it technically.
- Assumes `OpenTelemetry.Api` in implementation packages is acceptable per the conventions doc (verified: 80 KB, zero transitive dependencies on net10).
- Assumes the existing bridge tests (enricher pipeline, suppression toggles) can be ported to Core rather than rewritten from scratch.

## Outstanding Questions

**Deferred to planning**

- Q1. Consume-span relation for async/outbox flows: remote parent vs span link. The current bridge behavior is the parity baseline; semconv guidance for queue-based systems leans toward links for batch/delayed consumption — decide per transport when the emit sites are laid out.
- Q2. Fate of the `headless.messaging.send.duration_ms`/`persistence.duration_ms`/`receive.duration_ms`/`invoke.duration_ms` span attributes: drop (spans have intrinsic duration; metrics already carry these as histograms) vs keep under a unit-free name. Leaning drop.
- Q3. Exact enricher signature after the sync move: keep `ValueTask Enrich(...)` awaited-synchronously, or break to a synchronous `void Enrich(...)` — a breaking change is acceptable, so pick whichever makes the no-async contract impossible to misuse.

## Sources / Research

- Bridge implementation being replaced: `src/Headless.Messaging.OpenTelemetry/` (`DiagnosticListener.cs` — span building + propagation via `PropagationContext`/`Baggage`; `MessagingMetrics.cs` — semconv instruments; `MessagingTags.cs`; `IActivityTagEnricher.cs` — including the documented fire-and-forget async wart; `MessagingInstrumentationOptions.cs`).
- Emission sites in Core: `src/Headless.Messaging.Core/Internal/DirectPublisherCore.cs`, `ISubscribeExecutor.cs`, `IConsumerRegister.cs`.
- Convention basis and native/`OpenTelemetry.Api` rules: [docs/solutions/conventions/opentelemetry-instrumentation-conventions.md](../solutions/conventions/opentelemetry-instrumentation-conventions.md).
- Reference implementation shape: the caching plan ([2026-07-15-001](2026-07-15-001-feat-caching-otel-instrumentation-plan.md)) and `src/Headless.DistributedLocks.Core/RegularLocks/DistributedLocksDiagnostics.cs`.
