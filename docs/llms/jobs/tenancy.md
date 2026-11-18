---
domain: Jobs (Background Jobs)
packages:
---

# Run jobs per tenant

> Part of [Jobs](../jobs.md). Capture the tenant when a job is scheduled, restore it around every attempt, and fan a system-scope cron out to tenant-scoped jobs.

## Tenant propagation

Time jobs carry a persisted `TenantId` of at most `JobsTenancyOptions.TenantIdMaxLength` (200) characters. The tenant resolves once, when the job is scheduled, and is restored around every execution attempt, so a job scheduled from tenant `t1` runs its handler, and each retry, under `t1`. Update payloads, such as dashboard edits and seeder refreshes, cannot change or clear a stored tenant.

Enable it through the tenancy seam, the only place Jobs tenancy is configured:

```csharp
builder.AddHeadlessTenancy(tenancy =>
    tenancy.Jobs(jobs => jobs.PropagateTenant().RequireTenantOnEnqueue())
);
```

- `PropagateTenant()` captures the ambient tenant at schedule time.
- `RequireTenantOnEnqueue()` rejects a tenantless, non-system job.
- `RejectCrossTenantEnqueue()` rejects an explicit tenant that differs from the ambient one.

`JobsTenancyOptions` exposes the resulting flags read-only. Register a real `ICurrentTenant` source (HTTP claim resolution, `AddHeadlessDbContextServices()`, or your own) before `AddHeadlessJobs`. Without one, propagation reads the `NullCurrentTenant` fallback and captures nothing. [multi-tenancy.md](../multi-tenancy/background-work.md#background-jobs) covers registration and startup diagnostics with the other tenancy seams.

**Verify:** schedule a job while tenant `t1` is current, and assert inside the handler (or on the stored row's `TenantId`) that the tenant is `t1`. With `RequireTenantOnEnqueue()`, a schedule with no tenant throws `MissingTenantContextException`.

### Schedule-time rules

The schedule-time rules apply in this order:

1. **Cron is always system scope.** A cron definition with a non-null `TenantId` is rejected with `JobValidatorException`. Never give a cron definition a tenant.
2. **System jobs.** `JobOptions.IsSystemJob = true` (or `AsSystemJob()`) marks a deliberately tenantless job. It is rejected with `JobValidatorException` when it also carries an explicit `TenantId`, or when an ambient tenant is present, because tenant code cannot promote itself to system scope. `IsSystemJob` is never persisted.
3. **An explicit tenant wins.** `JobOptions.TenantId` (or the entity's `TenantId`) is used as given, even when it differs from the ambient tenant.
4. **Ambient capture.** With no explicit tenant and `PropagateTenant()` on, the ambient `ICurrentTenant.Id` is written to the row in the same write.
5. **Strict rejection.** A job still without a tenant, not a system job, under `RequireTenantOnEnqueue()` is rejected with `Headless.MultiTenancy.MissingTenantContextException`. Without strict mode it stays system scope.

Rules 1 and 2 and the bounds checks always run, even with the seam flags off. A blank or over-length tenant, explicit or ambient, rejects the schedule instead of falling back to system scope. The log records only its length, never the value.

**Cross-tenant scheduling.** An explicit tenant that differs from the ambient one logs a warning (`JobCrossTenantEnqueue`, or `JobChainDescendantCrossTenant` for a chain step) and is honored by default. Any in-process code can already switch tenants with `ICurrentTenant.Change`, so this guard catches accidents, such as a stale `TenantId` on a reused options object, not attackers. `RejectCrossTenantEnqueue()` turns the mismatch into a `JobValidatorException`. An explicit tenant supplied from system scope (no ambient tenant) is always honored, so cron fan-out keeps working.

**Chains.** Each chain descendant follows the root's rules. A descendant without a tenant inherits the root's resolved tenant. A descendant's own explicit `TenantId` wins and is validated. A descendant marked `IsSystemJob` is rejected when an ambient tenant is present, and otherwise stays tenantless.

### Execution-time restore

- Every attempt runs inside `ICurrentTenant.Change(tenantId)`, reverted when the attempt ends, so no tenant leaks between attempts or retries.
- A row with a persisted `TenantId` always runs under it, even with `PropagateTenant()` off. An explicitly tenanted job never runs as system scope by accident.
- With `PropagateTenant()` on, a row without a tenant clears any leaked ambient tenant, so a system job runs as system scope.
- The tenant is also restored around the failure callbacks (`IJobExceptionHandler`, the cancellation handler, and `OnExhausted`), so tenant-aware alerting and compensation run under the right tenant.
- A tenant-scoped job's execution span carries the tenant under `tenant.id` unless `TenantTelemetryOptions.EnrichTraces` is off ([multi-tenancy.md](../multi-tenancy/observability.md#observability)). The tenant is never a metric tag.

### Cron fan-out

Cron is system scope, so tenant-scoped recurring work is application code: a system-scope cron handler lists tenants and schedules one tenant-scoped time job per tenant with an explicit `JobOptions.TenantId`.

```csharp
using Headless.Jobs;

// A tenant-scoped time job. When it runs, ICurrentTenant is already the job's TenantId,
// so tenant-scoped services (EF global filters, permission cache) see the right tenant.
public sealed record TenantReportRequest(string ReportKind);

[Job("reports.tenant-report")]
public sealed class TenantReportJob(IReportService reports) : IJob<TenantReportRequest>
{
    public async ValueTask ExecuteAsync(JobContext<TenantReportRequest> context, CancellationToken ct) =>
        await reports.BuildAsync(context.Request.ReportKind, ct);
}

// A system-scope cron that fans out one tenant-scoped time job per tenant.
[Job("reports.nightly-fan-out", Cron = "0 0 2 * * *")]
public sealed class NightlyReportFanOutJob(
    IJobScheduler scheduler,
    IAppTenantDirectory tenants // application-owned enumeration
) : IJob
{
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken ct)
    {
        foreach (var tenantId in await tenants.ListActiveTenantIdsAsync(ct))
        {
            // An explicit TenantId is required: the cron handler runs as system scope, so there is no
            // ambient tenant for PropagateTenant() to capture. Relying on capture here persists tenantless jobs.
            await scheduler.EnqueueAsync(
                new TenantReportRequest("nightly"),
                new JobOptions { TenantId = tenantId, Description = $"nightly-report-{tenantId}" },
                ct
            );
        }
    }
}
```

`IAppTenantDirectory` stands for an application-owned tenant list. When the host configures a tenant catalog with `.Catalog(...)`, use the framework's `Headless.MultiTenancy.ITenantDirectory.GetAllAsync()` instead ([multi-tenancy.md](../multi-tenancy/background-work.md#cron-fan-out)). The framework ships no per-tenant cron rows or per-tenant cron expressions.

**Verify:** after one fan-out run, the store holds one time job per tenant, each with its `TenantId`.
