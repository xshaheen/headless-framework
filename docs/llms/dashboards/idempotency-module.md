---
domain: Dashboards
packages:
---

# Idempotency module

> Part of [Dashboards](../dashboards.md). The Idempotency module from `Headless.Idempotency.Dashboard`: registration, options, the store and lookup endpoints, the tenant rule, and what the screen shows for each record state.

## Register the module

```bash
dotnet add package Headless.Idempotency.Dashboard
```

`AddIdempotency()` adds the Idempotency module under the id `idempotency`, in the Platform product, with an Idempotency sidebar entry that has no count. It needs `AddHeadlessIdempotency(...)` in the same host: a host without it fails to start with a message naming that call.

```csharp
using Headless.Dashboard;
using Headless.Idempotency;

builder.Services.AddHeadlessIdempotency(setup => setup.UseInMemory());
builder.Services.AddHeadlessDashboard(dashboard => dashboard
    .WithHostAuthentication(host => { host.ViewPolicy = "ops.view"; host.OperatePolicy = "ops.operate"; })
    .AddIdempotency());

var app = builder.Build();
app.MapHeadlessDashboard();
```

**Verify:** a signed-in viewer's `GET {base}/api/idempotency/store` (by default `/headless/api/idempotency/store`) answers `200` with the provider and its defaults, and the Platform tab's sidebar shows Idempotency.

| `IdempotencyDashboardOptions` member | Default | Description |
| --- | --- | --- |
| `ViewPolicy`, `OperatePolicy` | the shell's | As for every module. A white-space name fails options validation at startup. The module is read-only, so only `ViewPolicy` gates it. |

`AddIdempotency(Action<IdempotencyDashboardOptions>)`, `AddIdempotency(Action<IdempotencyDashboardOptions, IServiceProvider>)`, and `AddIdempotency(IConfiguration)` configure them.

Every principal that passes the module's view policy reads any tenant's record metadata, as it reads any tenant's jobs and messages in those modules. Set a narrower `ViewPolicy` on this module when only some operators may look across tenants.

## API

| Route | Policy | Answers |
| --- | --- | --- |
| `GET {base}/api/idempotency/store` | view | `IIdempotencyInspector.Describe()` as `{ provider, location, defaultRetentionSeconds, defaultLeaseSeconds, minimumLeaseSeconds, maximumLeaseSeconds, purgeIntervalSeconds, purgeBatchSize }`. `location` is absent in memory; `purgeIntervalSeconds` is absent when the purge is off and on the cache provider. |
| `POST {base}/api/idempotency/lookup` | view | `{ tenantId, found, record }`. `record` is `{ status, generation, takeoverCount, fingerprint: { algorithm, hash }, leaseExpiresAt, isLeaseExpired, retentionUntil, observedAt, resultContract, resultSizeBytes, recoveryPoint, recoveryContract, recoveryStateSizeBytes }`, with `hash` as lowercase hex and every field the provider does not keep left out. |

The lookup body is `{ key, tenantId? }`. It is a POST so the key never reaches a URL or a request log, and the route answers no other method; send `X-Requested-With` as for every dashboard POST.

- The lookup reads in exactly the tenant the body names. An absent or `null` `tenantId` reads the host scope, never the request's ambient tenant and never every tenant. The module wraps `InspectAsync` in `ICurrentTenant.Change(tenantId)` and restores the request's tenant afterwards, also when the store fails.
- A missing key answers `200` with `found: false`, never `404`. The answer echoes `tenantId` and never the key.
- A missing, blank, or longer-than-256-character `key`, a key with surrounding white space or a NUL character, a `tenantId` that is empty, white space, or longer than 128 characters, or malformed JSON answers `400` naming `key`, `tenantId`, or `body`. No problem carries the submitted value.
- A store failure answers `503` with the fixed detail `The idempotency store is unavailable. Try again.` and logs a warning with only the exception type, because provider messages can quote the key and the tenant. A cancelled request is not a `503`.
- No route, command, or object kind releases, expires, deletes, or admits a record, and no answer carries the stored result or recovery-state bytes.

HTTP middleware records (`Headless.Idempotency.ResponseReplay`) are keyed by a SHA-256 of the request scope under the caller's tenant claim, so the client's `Idempotency-Key` header alone does not find them. Look up the keys your own code passes to `AdmitAsync`.

## The screen

Idempotency states the provider and where its records live, the default retention, the default lease with its bounds, and the purge interval; on the cache provider, or with the purge off, the purge reads `Off` with both explanations. A "Look up a key" form takes a Tenant (blank for the host scope) and an Idempotency key. The result names one state from the server's fields, judged against `observedAt`, never the browser clock:

| Record | Screen state |
| --- | --- |
| `status` `Completed` | Completed: later admissions replay the stored result |
| `status` `Pending`, `retentionUntil` after `observedAt` | In flight: a caller with the same key gets `InFlight` and retries later |
| `status` `Pending`, `retentionUntil` at or before `observedAt` | In flight, with a note that the record is kept past retention until the attempt ends |
| `isLeaseExpired` `true` | Stalled: the next admission takes the key over |
| `status` `Released`, `generation` present | Released: the next admission runs the operation again |
| `status` `Released`, no `generation` | Not admitted yet |

The result lists the key, the tenant, the generation, the takeover count (absent on the cache provider), the fingerprint, the lease expiry, the retention, and the contract and size of the stored result and recovery point, with an `As of` time and Look up again. A missing key shows a not-found state naming the tenant scope searched, and a `503` shows an error with Retry; neither shows an earlier result. Editing the key or the tenant clears the result, and the answer of a superseded lookup is never shown. The looked-up key has no URL by design.

The screen shows no admission or completion time, stored HTTP status, or replay count, because no provider stores them.
