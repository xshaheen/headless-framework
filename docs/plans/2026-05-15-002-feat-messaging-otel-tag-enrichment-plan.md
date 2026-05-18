---
id: 2026-05-15-002
title: "Messaging P1.2: OpenTelemetry activity tag enrichment"
status: active
created: 2026-05-15
issue: https://github.com/xshaheen/headless-framework/issues/230
depth: standard
origin: issue #230 (tag enrichment, tenant ID tagging, retry count tagging)
depends_on: PR #254 (retry policy + NextRetryAt — treat as merged)
---

# Messaging P1.2: OpenTelemetry activity tag enrichment

## Problem Frame

The current `DiagnosticListener` in `Headless.Messaging.OpenTelemetry` emits a fixed set of OTel tags per span type with no extension point. Applications cannot add custom tags (e.g. tenant context, feature flags) to messaging spans without forking the listener. Built-in framework-relevant tags — tenant ID and retry count — are also missing.

Goals:
1. Add `IActivityTagEnricher` extension point so apps can register custom tag writers called for every span.
2. Add built-in `headless.messaging.tenant_id` tag from the `headless-tenant-id` wire header.
3. Add built-in `headless.messaging.retry_count` tag on `subscriber.invoke` spans from `MediumMessage.Retries`.
4. Allow tenant ID tagging to be suppressed globally (shared-backend scenarios).
5. Expose retry count through `MessageEventDataSubExecute` so the subscriber.invoke span can include it.

Out of scope: `headless.messaging.completion` (delivery kind) — deferred to issue #232.

## Scope Boundary

**In scope:**
- `IActivityTagEnricher` interface + `MessagingEnrichmentContext` context type + `MessagingEventKind` enum
- `MessagingInstrumentationOptions` (replaces bare `bool enableMetrics` parameter)
- Default `TenantIdTagEnricher` (internal, registered unless suppressed)
- `DiagnosticListener` — enricher call sites + `ILogger` for enricher exception isolation
- `MessagingInstrumentation` — wires options to DiagnosticListener
- `Setup.cs` — new overload accepting `Action<MessagingInstrumentationOptions>?`
- `MessageEventDataSubExecute.RetryCount` — expose `MediumMessage.Retries` to the subscriber.invoke span
- `SubscribeExecutor._TracingBefore/After/Error` — pass `message.Retries` into event data

**Out of scope:** `headless.messaging.completion`, `DeliveryKind`, OpenTelemetry metric enrichment, enricher ordering guarantees beyond insertion order.

## Key Technical Decisions

### 1. `IActivityTagEnricher` lives in `Headless.Messaging.OpenTelemetry`, not Abstractions

`System.Diagnostics.Activity` is an OTel-specific type; putting the interface in Abstractions would add a non-obvious transitive dependency on `System.Diagnostics.DiagnosticSource`. The enricher is an OTel concern. Apps configure it during `AddMessagingInstrumentation(...)` — there is no need for it to be resolvable from core DI.

### 2. `MessagingEnrichmentContext` is a `readonly struct` passed `in`

Six scalar fields plus one dictionary reference — stack-friendly, no heap allocation per enricher call. Passed `in` so enrichers cannot replace the reference.

### 3. `MessagingInstrumentationOptions` absorbs `bool enableMetrics`

Breaking the existing `bool enableMetrics = false` parameter. This is acceptable for a greenfield library. Callers update to `options.EnableMetrics = true` inside the configure action.

The new signature:
```csharp
AddMessagingInstrumentation(TracerProviderBuilder builder, Action<MessagingInstrumentationOptions>? configure = null)
```

### 4. `ILogger` is injected via `Func<IServiceProvider, MessagingInstrumentation>` factory

`AddInstrumentation` has a `Func<IServiceProvider, T>` overload. Switch to it so `DiagnosticListener` can receive `ILogger<DiagnosticListener>` for enricher exception logging.

### 5. Built-in `TenantIdTagEnricher` is internal and registered by default

Applying `SuppressTenantIdTag = true` skips its registration. Not removable at runtime after build — configuration-time only. This mirrors how OpenTelemetry SDK instruments handle default behavior.

### 6. Retry count baked into `subscriber.invoke` span, not via enricher

`headless.messaging.retry_count` is set directly in `DiagnosticListener` (like other standard tags) when `RetryCount > 0`. Enrichers receive the retry count via `MessagingEnrichmentContext.RetryCount` for any custom tagging logic they need.

### 7. Enricher exception isolation

Each enricher is wrapped in its own try/catch. An enricher that throws logs a `Warning` via the injected logger and continues to the next enricher. Messaging is never interrupted.

### 8. Headers in context are the raw wire headers — untrusted

Per `Headers.cs` documentation, consume-side values are untrusted wire data. The plan does not sanitize them. Enrichers that write header values to sensitive sinks (SQL, URLs, logs) must sanitize at their own call site.

## Implementation Units

### Unit A — `MessageEventDataSubExecute.RetryCount` (`Headless.Messaging.Core`)

**File:** `src/Headless.Messaging.Core/Diagnostics/EventData.Message.S.cs`

Add `int RetryCount { get; set; }` to `MessageEventDataSubExecute`. Default `0` — safe for first delivery.

**File:** `src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs`

In `_InvokeConsumerMethodAsync(MediumMessage message, ...)`, all three tracing helpers (`_TracingBefore`, `_TracingAfter`, `_TracingError`) construct `MessageEventDataSubExecute`. Pass `message.Retries` as `RetryCount` in each construction.

Test file: `tests/Headless.Messaging.OpenTelemetry.Tests.Unit/DiagnosticListenerTests.cs`

Test scenarios:
- `should_set_retry_count_zero_on_first_delivery` — `MessageEventDataSubExecute { RetryCount = 0 }` → `subscriber.invoke` activity has no `headless.messaging.retry_count` tag
- `should_set_retry_count_on_persisted_retry` — `RetryCount = 3` → tag `headless.messaging.retry_count = 3` is present

### Unit B — OTel enrichment types (`Headless.Messaging.OpenTelemetry`)

**New files:**

`src/Headless.Messaging.OpenTelemetry/MessagingEventKind.cs`
```
enum MessagingEventKind { Persist, Publish, Consume, SubscriberInvoke }
```

`src/Headless.Messaging.OpenTelemetry/MessagingEnrichmentContext.cs`
```
readonly struct MessagingEnrichmentContext
{
    MessagingEventKind Kind
    string? MessageId
    string? MessageName       // topic / operation
    string? TenantId          // pre-extracted from Headers.TenantId header
    string? CorrelationId
    int RetryCount            // 0 for Persist/Publish/Consume; MediumMessage.Retries for SubscriberInvoke
    IReadOnlyDictionary<string, string?> Headers  // raw wire headers, untrusted on consume side
}
```

`src/Headless.Messaging.OpenTelemetry/IActivityTagEnricher.cs`
```
public interface IActivityTagEnricher
{
    void Enrich(Activity activity, in MessagingEnrichmentContext context);
}
```

`src/Headless.Messaging.OpenTelemetry/MessagingInstrumentationOptions.cs`
```
public sealed class MessagingInstrumentationOptions
{
    public bool EnableMetrics { get; set; } = false;
    public bool SuppressTenantIdTag { get; set; } = false;
    public IList<IActivityTagEnricher> Enrichers { get; } = [];
}
```

`src/Headless.Messaging.OpenTelemetry/Internal/TenantIdTagEnricher.cs` (internal sealed)
- Implements `IActivityTagEnricher`
- Sets `headless.messaging.tenant_id` from `context.TenantId` when not null/whitespace

No test file for types alone; coverage comes from DiagnosticListener tests.

### Unit C — `DiagnosticListener` enricher call sites (`Headless.Messaging.OpenTelemetry`)

**File:** `src/Headless.Messaging.OpenTelemetry/DiagnosticListener.cs`

Constructor changes:
```csharp
internal class DiagnosticListener(
    IReadOnlyList<IActivityTagEnricher> enrichers,
    ILogger<DiagnosticListener> logger,
    MessagingMetrics? metrics = null
)
```

Helper: `_CallEnrichers(Activity activity, in MessagingEnrichmentContext context)` — iterates `enrichers`, wraps each call in try/catch, logs `LogWarning` on exception.

Call `_CallEnrichers` after all standard tags are set in each `Before*` event handler:
- `BeforePublishMessageStore` → `Kind = Persist`, MessageId from `eventData.Message.GetId()`, TenantId from `eventData.Message.Headers`, RetryCount = 0
- `BeforePublish` → `Kind = Publish`, from `eventData.TransportMessage.*`, RetryCount = 0
- `BeforeConsume` → `Kind = Consume`, from `eventData.TransportMessage.*`, RetryCount = 0
- `BeforeSubscriberInvoke` → `Kind = SubscriberInvoke`, from `eventData.Message.*`, RetryCount = `eventData.RetryCount`

Also set `headless.messaging.retry_count` directly in `BeforeSubscriberInvoke` when `eventData.RetryCount > 0`.

Test file: `tests/Headless.Messaging.OpenTelemetry.Tests.Unit/DiagnosticListenerTests.cs`

Test scenarios (new):
- `should_call_enrichers_in_registration_order_on_publish` — verify call order
- `should_call_enrichers_on_all_span_types` — one enricher, verify called for Persist/Publish/Consume/SubscriberInvoke
- `should_provide_tenant_id_in_context_when_header_present` — context.TenantId populated from wire header
- `should_provide_null_tenant_id_when_header_absent`
- `should_provide_retry_count_in_context_for_subscriber_invoke`
- `should_continue_after_enricher_throws` — enricher 1 throws, enricher 2 still called, no exception propagated
- `should_log_warning_when_enricher_throws`

### Unit D — `Setup.cs` and `MessagingInstrumentation` wiring

**File:** `src/Headless.Messaging.OpenTelemetry/Setup.cs`

Replace `bool enableMetrics = false` overload with `Action<MessagingInstrumentationOptions>? configure = null`:
```csharp
public static TracerProviderBuilder AddMessagingInstrumentation(
    this TracerProviderBuilder builder,
    Action<MessagingInstrumentationOptions>? configure = null
)
```

Switch factory to `Func<IServiceProvider, MessagingInstrumentation>`:
```csharp
builder.AddInstrumentation(sp =>
{
    var options = new MessagingInstrumentationOptions();
    configure?.Invoke(options);
    var logger = sp.GetRequiredService<ILogger<DiagnosticListener>>();
    var enrichers = BuildEnricherList(options);
    var metrics = options.EnableMetrics ? new MessagingMetrics() : null;
    return new MessagingInstrumentation(new DiagnosticListener(enrichers, logger, metrics), metrics);
});
```

`BuildEnricherList` (private static helper): prepends `TenantIdTagEnricher` unless `options.SuppressTenantIdTag`, then appends `options.Enrichers`.

**File:** `src/Headless.Messaging.OpenTelemetry/MessagingInstrumentation.cs`

No structural change — just passes new `DiagnosticListener` constructor shape through. Constructor signature is unchanged externally since `DiagnosticListener` is internal.

Test file: `tests/Headless.Messaging.OpenTelemetry.Tests.Unit/SetupTests.cs`

Test scenarios (new):
- `should_register_default_tenant_id_enricher_when_not_suppressed`
- `should_not_register_tenant_id_enricher_when_suppressed`
- `should_include_custom_enrichers_after_default`
- `should_enable_metrics_when_option_set`
- `should_disable_metrics_by_default`

### Unit E — Built-in tenant ID tag (TenantIdTagEnricher)

`src/Headless.Messaging.OpenTelemetry/Internal/TenantIdTagEnricher.cs`

Adds `activity.SetTag("headless.messaging.tenant_id", context.TenantId)` when `context.TenantId` is not null/whitespace.

Test file: `tests/Headless.Messaging.OpenTelemetry.Tests.Unit/TenantIdTagEnricherTests.cs`

Test scenarios:
- `should_set_tenant_id_tag_when_tenant_present`
- `should_not_set_tag_when_tenant_id_null`
- `should_not_set_tag_when_tenant_id_whitespace`

## File Checklist

| File | Change |
|---|---|
| `src/Headless.Messaging.Core/Diagnostics/EventData.Message.S.cs` | Add `int RetryCount` to `MessageEventDataSubExecute` |
| `src/Headless.Messaging.Core/Internal/ISubscribeExecutor.cs` | Pass `message.Retries` as `RetryCount` in all three tracing helpers |
| `src/Headless.Messaging.OpenTelemetry/MessagingEventKind.cs` | NEW — `MessagingEventKind` enum |
| `src/Headless.Messaging.OpenTelemetry/MessagingEnrichmentContext.cs` | NEW — readonly struct |
| `src/Headless.Messaging.OpenTelemetry/IActivityTagEnricher.cs` | NEW — public interface |
| `src/Headless.Messaging.OpenTelemetry/MessagingInstrumentationOptions.cs` | NEW — public sealed class |
| `src/Headless.Messaging.OpenTelemetry/Internal/TenantIdTagEnricher.cs` | NEW — internal sealed enricher |
| `src/Headless.Messaging.OpenTelemetry/DiagnosticListener.cs` | Constructor + `_CallEnrichers` helper + 4 Before* call sites + retry_count tag |
| `src/Headless.Messaging.OpenTelemetry/MessagingInstrumentation.cs` | Minor: pass logger + enrichers to DiagnosticListener |
| `src/Headless.Messaging.OpenTelemetry/Setup.cs` | Replace `bool enableMetrics` → `Action<MessagingInstrumentationOptions>?`; switch to `Func<IServiceProvider, T>` factory |
| `tests/.../DiagnosticListenerTests.cs` | New enricher + retry count + tenant ID tag scenarios |
| `tests/.../SetupTests.cs` | New enricher registration scenarios |
| `tests/.../TenantIdTagEnricherTests.cs` | NEW — unit tests for built-in enricher |

## Sequencing

1. Unit A first — `MessageEventDataSubExecute.RetryCount` + `SubscribeExecutor` wiring. No OTel dependency.
2. Unit B — new types. No behavior, just contracts. Can be reviewed in isolation.
3. Unit C — `DiagnosticListener` enricher call sites. Requires A + B.
4. Unit D + E — Setup wiring + TenantIdTagEnricher. Requires B + C.
5. Tests for all units in parallel with implementation.

## Risks and Mitigations

| Risk | Mitigation |
|---|---|
| `Func<IServiceProvider, T>` factory requires correct OTel SDK version | Verify against the version pinned in `Directory.Packages.props` before assuming the overload exists; `AddInstrumentation(Func<IServiceProvider, T>)` was added in OTel SDK 1.7.x |
| `IReadOnlyDictionary` on `Headers` — avoid boxing the concrete type | Cast at construction site to `IReadOnlyDictionary<string, string?>` once; enrichers get the interface |
| `in` parameter on enricher interface — value type copy semantics | `MessagingEnrichmentContext` is a readonly struct; `in` prevents defensive copies. All fields are references or primitives — no mutable structs. |
| `message.Retries` is 0 for inline retries (only persisted pickups advance it) | Correct by design per PR #254 contract. Documented in `MessagingEnrichmentContext.RetryCount` XML doc. |

## Open Questions

1. Does the OTel SDK version in `Directory.Packages.props` expose `AddInstrumentation(Func<IServiceProvider, T>)`? Verify before coding Unit D.
2. Should `MessagingEnrichmentContext.Headers` be `IReadOnlyDictionary<string, string?>` or a custom type? The interface is simpler; a custom type could add a sanitize helper — probably YAGNI for now.
3. Should sibling `Persist` → `Publish` spans share a common parent context to indicate "this publish was via outbox"? This is the #232 delivery kind question; do not address here.
