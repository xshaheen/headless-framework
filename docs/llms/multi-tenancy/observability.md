---
domain: Multi-Tenancy
packages:
---

# Tenant observability

> Part of [Multi-Tenancy](../multi-tenancy.md). Find logs, spans, and metrics by tenant, and rename or turn off the tenant attributes.

## Observability

Logs carry the tenant id on every host that calls `AddHeadlessLogging()`, which `AddHeadless()` (ServiceDefaults) does for you. Spans carry it with `AddHeadless()` and OpenTelemetry. Neither needs a registration:

- **Logs.** Every log entry written while a tenant is ambient carries the field `TenantId`, read when the entry is written. Every logging provider gets it: the console, OpenTelemetry, and any provider you add. So a message consumer's entries and a job attempt's entries carry their tenant on a console-only host too. A nested `ICurrentTenant.Change(...)` relabels the entries inside it, `Change(null)` removes the label, and a hand-written per-tenant loop, hosted service, or migration is labelled with no extra call. An entry whose message template already has a `{TenantId}` placeholder, as the framework's own tenant-switch messages do, keeps that value and gets no second field.
- **Spans started under a tenant** get the tag `tenant.id`: database calls, outgoing HTTP, and your own spans.
- **Spans that start before their tenant is known** are tagged where the tenant is resolved:

| Span | Tagged by | Tenant source |
|---|---|---|
| ASP.NET Core request span | `UseHeadlessTenancy()` (`ResolveFromClaims`) | The tenant claim, on claim-only hosts |
| ASP.NET Core request span | `UseHeadlessTenantCatalogResolution()` | The catalog-resolved tenant, tagged before the claim check, so a rejected request is still attributed |
| Every messaging span (persist, publish, consume, subscriber invoke) | Messaging telemetry, when the span starts | The envelope's tenant header |
| Job execution span | Jobs instrumentation, when the span starts | The job's stored tenant |

A request, message, or job with no tenant gets no attribute. `ICurrentTenant.Change` writes no telemetry itself.

`AddHeadlessLogging()` adds the log field, and only ServiceDefaults adds the child-span enrichment. A host that builds its own OpenTelemetry pipeline still gets the request, messaging, and job span tags, but no tags on other spans. A host that builds its own logging without `AddHeadlessLogging()` gets no tenant field: add an `ILogEnricher` (`services.AddLogEnricher<T>()`) that reads `ICurrentTenant.Id`.

Do not add a `TenantId` field of your own beside `AddHeadlessLogging()`. An `ILogEnricher` cannot see the other enrichers' fields, so the entry would carry the key twice, which an OTLP backend rejects. To write the tenant yourself, set `EnrichLogs = false` first.

### Names

- `tenant.id` (span attribute): OpenTelemetry semantic conventions (checked at v1.44.0) register no general tenant attribute, and the proposal for one ([semantic-conventions#162](https://github.com/open-telemetry/semantic-conventions/issues/162)) is still open. `tenant.id` follows the `namespace.attribute` shape and the `tenant.*` namespace that proposal discusses.
- `TenantId` (log attribute): the same name as the `{TenantId}` placeholder in the framework's own log messages, so one query finds both.

There is no separate `headless.messaging.tenant_id` or `headless.job.tenant_id`. `EnrichTraces` is the one switch for all spans.

### Metrics

Framework meters carry no tenant dimension by default. The messaging inbox metrics have `MessagingInstrumentationOptions.IncludeTenantIdInMetricTags`, off by default, which uses `AttributeName` as the dimension name. To tag your own meter, add `currentTenant.Id` under the same name. Each distinct tenant id becomes its own time series, so do it only when the tenant count is bounded and your metrics backend can hold that many series. Otherwise analyze per tenant in logs and traces.

Anyone with access to the telemetry store sees every tenant's ids. Route telemetry per tenant, or turn a channel off, when tenant ids must not be shared.

### Configuration

`TenantTelemetryOptions` controls every channel. The defaults apply with no call. Change them through the root builder:

```csharp
builder.AddHeadlessTenancy(tenancy =>
    tenancy
        .Http(http => http.ResolveFromClaims())
        .Telemetry(telemetry =>
        {
            telemetry.AttributeName = "app.tenant.id"; // span attribute and inbox metric dimension
            telemetry.LogAttributeName = "Tenant";
        })
);
```

| Option | Default | Effect |
|---|---|---|
| `EnrichLogs` | `true` | Adds the tenant field to log entries written while a tenant is ambient, on every provider, once `AddHeadlessLogging()` is called |
| `LogAttributeName` | `TenantId` | The log entry field name |
| `EnrichTraces` | `true` | Tags the request, messaging, and job spans, and every span started while a tenant is ambient |
| `AttributeName` | `tenant.id` | The span attribute name, also the messaging inbox metric dimension |

A blank `AttributeName`, or a blank `LogAttributeName` while `EnrichLogs` is on, fails options validation at startup. To tag a span that started before your code knew its tenant, call `TenantTelemetry.TagActivity(activity, options, tenantId)`.

**Verify:** in a test host that calls `AddHeadlessLogging()` and adds a capturing `ILoggerProvider`, write a log entry inside `currentTenant.Change("acme")` and assert it carries the field `TenantId` (or your `LogAttributeName`) once, with value `acme`.
