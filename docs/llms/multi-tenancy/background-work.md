---
domain: Multi-Tenancy
packages:
---

# Tenancy in background work

> Part of [Multi-Tenancy](../multi-tenancy.md). Carry the tenant into jobs, message consumers, terminal callbacks, and SignalR hubs, where HTTP middleware does not run.

## Non-HTTP Execution Paths

HTTP middleware sets the tenant for the request only. Jobs, message consumers, and SignalR hub calls each need their own tenant source. Outside a seam, set it yourself with `using (currentTenant.Change(tenantId)) { ... }`, or with `currentTenant.EnterOrVerify(tenantId)` when the work may already run under a tenant.

### Background Jobs

`Headless.Jobs` stores a `TenantId` on each time job. The tenant is resolved once, when the job is scheduled, and restored around every execution attempt, so a job scheduled under tenant `t1` runs its handler, and each retry, under `t1`. Cron jobs never carry a tenant ([Cron Fan-Out](#cron-fan-out)). [Run jobs per tenant](../jobs/tenancy.md) has the full schedule-time rules, chain behavior, and restore details.

```csharp
using Headless.Jobs;

builder.AddHeadlessTenancy(tenancy =>
    tenancy
        .Http(http => http.ResolveFromClaims()) // the ambient tenant that propagation captures
        .Jobs(jobs => jobs.PropagateTenant().RequireTenantOnEnqueue())
);

builder.Services.AddHeadlessJobs(options =>
{ /* ... */
});
```

Register a real `ICurrentTenant` source (HTTP claim resolution, `AddHeadlessDbContextServices()`, or your own) before `AddHeadlessJobs`. Otherwise propagation reads the `NullCurrentTenant` fallback and captures nothing.

**Verify:** schedule a job while tenant `t1` is current and assert `ICurrentTenant.Id == "t1"` inside the handler. With `RequireTenantOnEnqueue()`, scheduling with no tenant throws `MissingTenantContextException`.

#### Automatic Propagation (`PropagateTenant`)

- **At schedule time:** when the enqueue sets no `JobOptions.TenantId` (or entity `TenantId`), the ambient `ICurrentTenant.Id` is written to the job row in the same write that saves the job. Nothing captures it later.
- **At execution time:** every attempt runs inside `ICurrentTenant.Change(job.TenantId)`, ended on success, failure, or cancellation, so each retry gets a fresh scope.

An explicit `JobOptions.TenantId` always wins over the ambient tenant, even when they differ: the mismatch logs a warning. To reject an explicit tenant that differs from a present ambient tenant, add `RejectCrossTenantEnqueue()`. System-scope code (no ambient tenant), such as a cron fan-out, can still pass explicit tenants.

#### Strict Enqueue (`RequireTenantOnEnqueue`)

`RequireTenantOnEnqueue()` rejects a time-job enqueue that resolves no explicit and no ambient tenant with `Headless.MultiTenancy.MissingTenantContextException`, unless the job is a system job.

Set `IsSystemJob = true` (on `JobOptions` or the entity) to schedule a deliberately tenantless job. It is rejected with `JobValidatorException` when an ambient tenant is present or when an explicit `TenantId` is also set, so tenant code cannot promote itself to system scope. The decision is logged at schedule time, and `IsSystemJob` is never stored.

These checks always run, with or without the seam flags: tenant-scoped cron is rejected, the system-job conflicts are rejected, and a blank or over-length explicit tenant is rejected. The flags gate only ambient capture (`PropagateTenant`) and missing-tenant rejection (`RequireTenantOnEnqueue`).

#### Manual Propagation

Without `PropagateTenant()`, pass the tenant on every enqueue. A stored `JobOptions.TenantId` is restored around the handler regardless of the flag, so do not restore it inside the handler. Restore it yourself only for work outside the Jobs execution pipeline:

```csharp
// Explicit capture at schedule time — no ambient dependency. The handler runs under `tenantId`
// even with PropagateTenant() off, because a persisted tenant is always restored at execute time.
await scheduler.EnqueueAsync(request, new JobOptions { TenantId = tenantId }, ct);

// Inline work OUTSIDE the Jobs execute pipeline still needs an explicit scope.
using (currentTenant.Change(tenantId))
{
    await processor.RunAsync();
}
```

#### Cron Fan-Out

Cron definitions and occurrences are always system scope. Scheduling a cron definition with a non-null `TenantId` throws `JobValidatorException`. For recurring per-tenant work, list tenants in a system-scope cron handler and schedule one time job per tenant with an explicit `JobOptions.TenantId`:

```csharp
using Headless.Jobs;
using Headless.MultiTenancy; // ITenantDirectory — only when a tenant catalog is configured

// A tenant-scoped time job. When it runs, the execute middleware has already restored
// ICurrentTenant to the job's TenantId, so tenant-scoped services (EF global filters,
// permission cache) observe the right tenant automatically.
public sealed record TenantReportRequest(string ReportKind);

[Job("reports.tenant-report")]
public sealed class GenerateTenantReportJob(IReportService reports) : IJob<TenantReportRequest>
{
    public async ValueTask ExecuteAsync(JobContext<TenantReportRequest> context, CancellationToken ct) =>
        await reports.BuildAsync(context.Request.ReportKind, ct);
}

// A system-scope cron that fans out one tenant-scoped time job per tenant.
// ITenantDirectory is available only when a tenant catalog store is configured with .Catalog(...).
// Without a catalog, list tenants your own way; the loop is the same.
[Job("reports.nightly-fan-out", Cron = "0 0 2 * * *")]
public sealed class NightlyReportFanOutJob(IJobScheduler scheduler, ITenantDirectory tenants) : IJob
{
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken ct)
    {
        foreach (var tenant in await tenants.GetAllAsync(ct))
        {
            if (!tenant.IsEnabled)
            {
                continue;
            }

            // Explicit TenantId is REQUIRED: the cron handler runs system-scope, so there is no
            // ambient tenant for PropagateTenant() to capture here. Relying on ambient capture
            // inside a cron handler would silently persist tenantless jobs.
            await scheduler.EnqueueAsync(
                new TenantReportRequest("nightly"),
                new JobOptions { TenantId = tenant.Id, Description = $"nightly-report-{tenant.Id}" },
                ct
            );
        }
    }
}
```

`ITenantDirectory.GetAllAsync()` is implemented by every shipped catalog store (in-memory, configuration, EF Core) and exists only when a catalog is configured. It returns the tenant list and nothing more: Headless ships no per-tenant cron rows, per-tenant cron expressions, batching, or fan-out scheduling. If your application already has its own tenant-directory abstraction with a similar name, keep the two apart: only the framework's `ITenantDirectory` reads the catalog stores.

**Verify:** after one fan-out run, the job store holds one time job per enabled tenant, each with its `TenantId`.

#### Startup Diagnostics

The Jobs seam adds these checks to the tenancy startup validation:

| Code | Severity | Fires when |
|---|---|---|
| `HEADLESS_TENANCY_JOBS_REQUIRE_TENANT_ISOLATED` | Warning | `RequireTenantOnEnqueue()` is on, Jobs propagation is off, and no other seam or custom `ICurrentTenant` supplies an ambient tenant. Every non-system enqueue without an explicit tenant would fail. A warning, so a host that always passes `TenantId` still starts. |
| `HEADLESS_TENANCY_JOBS_PROPAGATION_NULL_CURRENT_TENANT` | Error | `PropagateTenant()` is on but no tenant source is registered, so `ICurrentTenant.Id` is always null and propagation would capture nothing. |
| `HEADLESS_TENANCY_JOBS_REQUIRE_TENANT_DISABLED` | Error | The seam asked for `RequireTenantOnEnqueue()` but `JobsTenancyOptions.TenantContextRequired` resolved to `false`, usually because a later `Configure<JobsTenancyOptions>` overwrote it. |
| `HEADLESS_TENANCY_JOBS_REJECT_CROSS_TENANT_DISABLED` | Error | The seam asked for `RejectCrossTenantEnqueue()` but `JobsTenancyOptions.RejectCrossTenantEnqueue` resolved to `false`, for the same reason. |

### Message Consumers

The envelope's `TenantId` comes from the `headless-tenant-id` wire header (`Headers.TenantId`). When you publish, set `PublishOptions.TenantId`, never the header. A raw header write with no typed value, or one that disagrees with the typed value, throws `InvalidOperationException`. A raw write equal to the typed value is accepted and changes nothing. On the consume side, the value is untrusted wire data: validate it before you use it beyond the tenant scope.

#### Automatic Propagation

```csharp
using Headless.Messaging;

builder.AddHeadlessTenancy(tenancy =>
    tenancy
        .Http(http => http.ResolveFromClaims()) // the ambient tenant that propagation captures
        .Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
);

builder.Services.AddHeadlessMessaging(options =>
{ /* ... */
});
```

`PropagateTenant()` adds `TenantPropagationPublishMiddleware` and `TenantPropagationConsumeMiddleware` to both messaging lanes. A Bus publish and a Queue enqueue stamp the ambient tenant. A Bus consumer, a Queue consumer, and a request-reply responder run inside the envelope tenant's scope, which ends on success and on failure. An explicit `PublishOptions.TenantId` or `QueueOptions.TenantId` is kept as set. A system message overrides propagation by setting `TenantId` explicitly or by publishing with no ambient tenant. Configure propagation only through this seam. Calling it more than once is safe. See [Tenant propagation](../messaging/middleware.md#tenant-propagation).

**Trust boundary.** The consume middleware trusts the envelope, and Headless assumes the bus is internal. For message names that external producers can publish, validate or sign the envelope before this middleware. Otherwise anyone who can publish to the bus can act as any tenant.

**Verify:** in a consumer test, read `ICurrentTenant.Id` inside the handler and assert it equals the publisher's tenant. A host with `PropagateTenant()` and no tenant source fails startup with `HEADLESS_TENANCY_MESSAGING_PROPAGATION_NULL_CURRENT_TENANT`.

#### Manual Propagation

Without `PropagateTenant()`, or when you need finer control, set the scope inside the consumer:

```csharp
var tenantId = context.TenantId;

using (currentTenant.Change(tenantId))
{
    await handler.HandleAsync(message, cancellationToken);
}
```

#### Strict Publish Tenancy (`TenantContextRequired`)

`.RequireTenantOnPublish()` requires every publish to resolve a tenant. The publish checks `PublishOptions.TenantId` first, then the ambient `ICurrentTenant.Id`. If neither has a value, the publish throws `Headless.MultiTenancy.MissingTenantContextException`.

Framework system messages are exempt. A publish marked `MessageOptions.IsSystemMessage = true` with no explicit and no ambient tenant goes out with no tenant header. Headless marks its own host-level messages this way: hybrid-cache invalidations (so catalog cache evictions and pre-authentication lookups still reach peers), distributed-lock release signals, and the Features, Settings, Permissions grant, and dynamic permission definition change announcements. The exemption never drops an existing tenant: an ambient tenant is still stamped, and a system message that would suppress one is rejected. Leave the flag unset on application messages. See [Strict Publish Tenancy](../messaging/publishing.md#strict-publish-tenancy).

`MessagingOptions.TenantContextRequired` reports the state, defaults to `false`, and has no public setter. The raw-header rules above always run before this check.

To fix a `MissingTenantContextException` in a background worker or `IHostedService`:

```csharp
// Option A: explicit per-publish tenant
await publisher.PublishAsync(message, new PublishOptions { TenantId = tenantId }, cancellationToken);

// Option B: ambient scope around the publish
using (currentTenant.Change(tenantId))
{
    await publisher.PublishAsync(message, cancellationToken);
}
```

Option B needs a real `ICurrentTenant`. The one `AddHeadless()` or `AddHeadlessDbContextServices()` registers is enough.

The Messaging seam reports these startup diagnostics:

| Code | Severity | Fires when |
|---|---|---|
| `HEADLESS_TENANCY_MESSAGING_REQUIRE_TENANT_ISOLATED` | Warning | `RequireTenantOnPublish()` is on and no other seam or custom `ICurrentTenant` supplies an ambient tenant. |
| `HEADLESS_TENANCY_MESSAGING_PROPAGATION_NULL_CURRENT_TENANT` | Error | `PropagateTenant()` is on but no tenant source is registered. |
| `HEADLESS_TENANCY_MESSAGING_REQUIRE_TENANT_DISABLED` | Error | The seam asked for `RequireTenantOnPublish()` but `MessagingOptions.TenantContextRequired` resolved to `false`. |

**Verify:** with `RequireTenantOnPublish()`, a publish with no explicit and no ambient tenant throws `MissingTenantContextException`.

### Messaging Terminal Callbacks

With messaging propagation on, terminal callbacks run with `ICurrentTenant` restored from the envelope before `RetryPolicy.OnTerminal` is called. This covers every terminal reason on publish and consume, including messages poisoned on arrival that never reach a consumer. `TerminalMessageInfo.TenantId` carries the envelope tenant whether or not propagation is on. A missing, white-space, or oversized tenant header resolves to no tenant, as on the consume side.

### SignalR

SignalR hub calls after the upgrade request run in new execution flows, and the HTTP middleware's tenant does not carry over to them. Set the tenant per hub call, for example in an `IHubFilter`.
