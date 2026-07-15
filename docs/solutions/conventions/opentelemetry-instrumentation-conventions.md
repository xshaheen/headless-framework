---
title: OpenTelemetry Instrumentation Conventions — Native Emission, Naming, and PII
date: 2026-07-15
last_updated: 2026-07-15
category: conventions
module: headless-framework
problem_type: convention
component: tooling
severity: medium
related_components:
  - caching
  - messaging
  - distributed-locks
  - jobs
tags:
  - opentelemetry
  - observability
  - telemetry
  - instrumentation
  - naming
  - metrics
  - tracing
  - conventions
---

# OpenTelemetry Instrumentation Conventions

How Headless subsystems emit OpenTelemetry metrics and traces: which emission model to use, how to name instruments and attributes, and how to handle sensitive data. Decided while scoping caching OTel (issue #384) and verified against the OpenTelemetry specification. See the fuller rationale in [docs/plans/2026-07-15-001-feat-caching-otel-instrumentation-plan.md](../../plans/2026-07-15-001-feat-caching-otel-instrumentation-plan.md).

## Context

The framework had grown two different OTel architectures without a stated rule for which to use:

- **Native model** — `Headless.DistributedLocks` and `Headless.Jobs` emit `Activity`/`Meter` directly through BCL `System.Diagnostics` primitives from their Core packages; consumers subscribe by name (`AddMeter("Headless.DistributedLocks")` / `AddSource(...)`). No `OpenTelemetry.*` dependency in the feature package.
- **Bridge model** — `Headless.Messaging.Core` emits `System.Diagnostics.DiagnosticSource` events (`DirectPublisherCore.cs`, `ISubscribeExecutor.cs`, `IConsumerRegister.cs`), and a separate `Headless.Messaging.OpenTelemetry` package subscribes to them, translates to `Activity` spans, runs a public `IActivityTagEnricher` pipeline, and does W3C context propagation.

Naming had drifted too: distributed-locks uses a **bare** metric dimension `reason` (`src/Headless.DistributedLocks.Core/RegularLocks/DistributedLockMetrics.cs:42`), while messaging **namespaces** its custom attributes (`headless.messaging.tenant_id`, `headless.messaging.retry_count`). Adding caching OTel forced the question: which model and which naming does a *new* subsystem follow?

## Guidance

### 1. Native emission everywhere; `OpenTelemetry.Api` only where used; no satellite packages

Emit `Activity`/`Meter` natively from the feature's Core package via BCL primitives. The OpenTelemetry **SDK** (`OpenTelemetry` package) is never referenced by any Headless package — only consuming apps reference it. `OpenTelemetry.Api` MAY be referenced by **implementation** packages (never `*.Abstractions`) where its APIs are actually used: cross-process propagation (`Propagators`, `PropagationContext`, `Baggage`) and the typed registration builders (`TracerProviderBuilder` / `MeterProviderBuilder`). Verified: `OpenTelemetry.Api` 1.16.0 is a single 80 KB dll with **zero transitive dependencies on net10.0** — not a heavy dependency — and it defines the abstract `TracerProviderBuilder`/`MeterProviderBuilder` types **including** the `AddSource(string[])`/`AddMeter(string[])` methods the typed helpers call (confirmed against `OpenTelemetry.Api.xml` 1.16.0). Helpers must stay on that API-level surface: SDK-only members — e.g. the deferred `AddInstrumentation(sp => ...)` overloads messaging's current bridge uses, which is why its csproj references the SDK today — are off-limits in Headless packages. Expose the instrumentation name as a `public const string`, and ship a typed `Add<Feature>Instrumentation()` extension on `TracerProviderBuilder`/`MeterProviderBuilder` in the feature's Core (a thin wrapper over `AddSource`/`AddMeter` with that const) so registration DX is uniform across subsystems; manual `AddMeter(name)`/`AddSource(name)` by const remains equally supported. Use the shared `HeadlessDiagnostics.CreateMeter` / `CreateActivitySource` helper (`src/Headless.Extensions/Constants/HeadlessDiagnostics.cs`, namespace `Headless.Constants`) so every subsystem's Meter and ActivitySource is named `Headless.<Subsystem>` at the assembly version.

This is the official .NET **library**-instrumentation guidance: a library should emit through `System.Diagnostics` `ActivitySource`/`Meter` and depend on at most the OpenTelemetry **API** package, never the SDK ([OTel .NET instrumentation docs](https://opentelemetry.io/docs/languages/net/instrumentation/)). Native `Activity`/`Meter` already deliver both benefits people reach for the bridge to get — Core stays OTel-free (they are BCL types) and *any* consumer subscribes (`ActivityListener`, App Insights, `dotnet-counters`, OTel), not only OTel.

A separate `Headless.<Feature>.OpenTelemetry` package is textbook-justified only when you **cannot** modify the source to emit natively (third-party libraries — the reason `OpenTelemetry.Instrumentation.Http`/`SqlClient` exist). Since Headless owns all its code, that never applies: **no subsystem gets a satellite package**. Messaging's `DiagnosticSource`→span bridge (`Headless.Messaging.OpenTelemetry`) is a legacy-shaped exception scheduled for **migration to native** ([docs/plans/2026-07-15-002-refactor-messaging-native-otel-plan.md](../../plans/2026-07-15-002-refactor-messaging-native-otel-plan.md)). Both features once thought to justify it are portable: its cross-process propagation already uses the `OpenTelemetry.Api` propagation types (which relocate into `Messaging.Core` unchanged now that implementation packages may reference `OpenTelemetry.Api`), and the `IActivityTagEnricher` API needs only the `Activity` + context, so it moves into Core — where running enrichers synchronously at span start also fixes the documented fire-and-forget async-enricher wart.

### 2. Naming: follow a semantic convention where one exists; otherwise bespoke `headless.<subsystem>.*`

- Use an official OpenTelemetry **semantic convention** when one exists and fits the domain. Messaging is the framework's living **hybrid** example: its metric instruments and standard dimensions are pure semconv (`messaging.publish.messages`, `messaging.consume.duration`, dims `messaging.operation` / `messaging.system` / `messaging.consumer.group` / `error.type` in `MessagingMetrics.cs`), while its framework-specific span attributes use bespoke `headless.messaging.*` (intent, tenant_id, retry_count). The Meter/ActivitySource itself is still named `Headless.Messaging` per the framework rule — only the *instrument and standard-attribute* names follow semconv. Note the messaging conventions are still **"Development"** status as of mid-2026; only `server.*`, `error.type`, `network.*` are Stable — pin the version and expect churn.
- Otherwise use a bespoke `headless.<subsystem>.*` namespace. **There is no OpenTelemetry cache semantic convention** (only proposal [semantic-conventions#1747](https://github.com/open-telemetry/semantic-conventions/issues/1747), unadopted; Redis is modeled as a database, which carries no hit/miss semantics), and none for distributed locks or job scheduling. For these, bespoke names are not a compromise — they are the OTel-recommended approach for non-standard telemetry (namespace by a reverse-domain or app-unique prefix, per the [OTel naming spec](https://opentelemetry.io/docs/specs/semconv/general/naming/)).

Metric-name mechanics (verified against the OTel naming + metrics specs): lowercase, dot-delimited namespaces, `snake_case` for multi-word segments, pluralize **only** countable instruments, **no `_total` suffix**, and units go in instrument metadata (`unit: "ms"`) never in the name (`headless.cache.factory.duration` + `unit: ms`, not `...duration_ms`). Never prefix with `otel.*` or an existing semconv namespace.

### 3. Namespace framework-owned attributes; fix the outlier

Framework-owned attribute keys are namespaced `headless.<subsystem>.*` (messaging already does this). Distributed-locks' bare `reason` dimension is the inconsistency to correct in a convention pass. Adopt standard semconv attribute names (`server.address`, `error.type`, `messaging.*`) only where the domain has a convention.

### 4. Sensitive data: opt-in on spans, never on metrics

PII / sensitive tags are opt-in or suppressible and never emitted wholesale. Keys, tenant identifiers, and connection strings are never metric dimensions (cardinality + privacy — you cannot un-leak PII from a trace backend). On spans they appear only behind an explicit toggle: caching's `IncludeKeyInTraces` (default **off**), messaging's `SuppressTenantIdTag`. This is stricter than some third-party libraries (FusionCache puts the raw key on every span with no redaction) and is the right default for a general-purpose framework. Low-cardinality non-sensitive identity (`headless.cache.name`, sanitized `BrokerAddress`) is always safe to attach.

## Why This Matters

- **Zero lock-in, fewer packages.** Native emission keeps the OpenTelemetry SDK out of every package and avoids one satellite package per subsystem — directly serving the framework's unopinionated, zero-lock-in charter. The only OTel reference anywhere is the 80 KB dependency-free `OpenTelemetry.Api`, in implementation packages that actually use it.
- **Near-zero overhead when unobserved.** BCL `Meter`/`ActivitySource` short-circuit via `Counter.Enabled` / `HasListeners()` when nothing is subscribed, so instrumentation costs nothing until an exporter attaches. Subscribing *is* the enable toggle — no separate `EnableMetrics` flag needed.
- **Consistent, tool-friendly names.** A single naming rule (semconv-where-it-exists, else `headless.*`) means dashboards and alerts built on one subsystem transfer to the next, and namespaced attributes avoid collisions with SDK-emitted tags.
- **Privacy is a default, not a footgun.** Opt-in key/PII tagging prevents a trace backend from silently accumulating tenant identifiers that cannot be retracted.

## When to Apply

- Adding OTel to any Headless subsystem (the immediate driver: caching #384; live/parked adjacent work: jobs OTel-in-middleware #319).
- Reviewing or naming new metric instruments, ActivitySource spans, or telemetry attributes anywhere in the framework.
- Deciding whether a subsystem needs a `.OpenTelemetry` satellite package — the answer is no, unconditionally; even messaging's is scheduled for removal by the native migration plan.
- Cleaning up existing telemetry. Two known outliers: distributed-locks' bare `reason` dimension (should be namespaced), and messaging span attributes that embed the unit in the name (`headless.messaging.send.duration_ms`, `...persistence.duration_ms` in `MessagingTags.cs`) — the unit belongs in metadata, not the attribute name. Messaging's *metric* instruments already do this correctly (`messaging.publish.duration`, no `_ms` suffix); only the span attributes carry the smell.

## Examples

Native subsystem (the default — distributed-locks shape, applied to a new subsystem):

```csharp
// In Headless.<Feature>.Core, BCL primitives only — no OpenTelemetry.* reference.
public static class Headless<Feature>Instrumentation
{
    // Public so consumers reference the symbol, not a magic string.
    // (distributed-locks trapped this const inside an internal class — avoid that.)
    public const string InstrumentationName = HeadlessDiagnostics.Prefix + "<Feature>";
}

// Typed registration helper in the same Core — OpenTelemetry.Api only (80 KB, dep-free on net10):
public static TracerProviderBuilder Add<Feature>Instrumentation(this TracerProviderBuilder builder) =>
    builder.AddSource(Headless<Feature>Instrumentation.InstrumentationName);

public static MeterProviderBuilder Add<Feature>Instrumentation(this MeterProviderBuilder builder) =>
    builder.AddMeter(Headless<Feature>Instrumentation.InstrumentationName);

// Consumer wiring — typed helper or subscribe-by-name, both first-class:
tracerProviderBuilder.Add<Feature>Instrumentation();
meterProviderBuilder.AddMeter(Headless<Feature>Instrumentation.InstrumentationName);
```

Naming — bespoke where no semconv exists (caching), with correct mechanics:

```text
headless.cache.requests              Counter    dims: headless.cache.operation, headless.cache.outcome, headless.cache.tier
headless.cache.factory.duration      Histogram  unit: ms   (NOT ...duration_ms)
headless.cache.failsafe.activations   Counter    dims: headless.cache.trigger
```

Attribute namespacing — the fix for the outlier:

```csharp
// Before (distributed-locks, DistributedLockMetrics.cs:42): bare dimension
[Counter<int>("reason", Name = "headless.lock.failed")]

// After (convention): namespaced, consistent with headless.messaging.*
[Counter<int>("headless.lock.reason", Name = "headless.lock.failed")]
```

## Related

- [docs/plans/2026-07-15-002-refactor-messaging-native-otel-plan.md](../../plans/2026-07-15-002-refactor-messaging-native-otel-plan.md) — the messaging bridge→native migration this convention schedules.
- [conventions/cross-package-structure-conventions.md](cross-package-structure-conventions.md) — the `Headless.<Feature>.*` package-naming taxonomy the `.OpenTelemetry` bridge-package name and `headless.<subsystem>.*` scheme extend; keep the two naming rules coherent.
- [concurrency/circuit-breaker-transport-thread-safety-patterns.md](../concurrency/circuit-breaker-transport-thread-safety-patterns.md) — Pattern 7's OTel metric-cardinality guard (pre-registered instruments, bounded tag sets, no runtime-derived tag values) is the safety companion to these naming rules.
- [guides/messaging-transport-provider-guide.md](../guides/messaging-transport-provider-guide.md) — `BrokerAddress` sanitization is a concrete instance of "never put credentials/PII on telemetry surfaces."
