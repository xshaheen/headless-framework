# feat: Messaging Scheduling — Hardening & Remaining Gaps

> **Scope:** Remaining abstractions + core gaps identified after Phase 1+2 completion. Covers stale job recovery, execution timeout, one-time job API, InMemory storage, execution purge, scheduling-only mode, handler resolution optimization, misfire strategy, health checks, and cron configuration override.
>
> **Prerequisites:** Phase 1+2 (US-001 through US-016) are complete and merged.
>
> **Out of scope:** Dashboard consolidation, Ticker package deprecation, SqlServer storage.

## Overview

Phase 1+2 delivered the core scheduling abstractions and infrastructure. This phase fills the remaining gaps that Ticker had (stale recovery, timeout enforcement) and addresses operational needs (execution purge, InMemory testing, scheduling-only mode).

**What changes:**
- New methods on `IScheduledJobStorage`: `ReleaseStaleJobsAsync`, `PurgeExecutionsAsync`
- New method on `IScheduledJobManager`: `ScheduleOnceAsync`
- `Timeout` property on `RecurringAttribute` and `ScheduledJob`
- `MisfireStrategy` enum and property on `RecurringAttribute`, `ScheduledJob`, and `ScheduledJobDefinition`
- `StaleJobRecoveryService` background service in Core
- Timeout enforcement in `SchedulerBackgroundService`
- Misfire detection in `SchedulerBackgroundService` before dispatch
- `SchedulerHealthCheck` implementing `IHealthCheck`
- Cron override from `IConfiguration` in `SchedulerJobReconciler`
- `InMemoryScheduledJobStorage` in `Headless.Messaging.InMemoryStorage`
- `PostgreSqlScheduledJobStorage` extended with new methods
- Scheduling-only mode validation (no transport required)
- Compiled delegate caching in `ScheduledJobDispatcher`

---

## Technical Approach

### Stale Job Recovery

Ticker had `TickerQFallbackBackgroundService` to recover jobs stuck in `Running` status (node crash, OOM, etc.). Currently if a Messaging scheduler node dies mid-execution, jobs remain `Running` forever.

```
StaleJobRecoveryService (BackgroundService):
  loop:
    count = await storage.ReleaseStaleJobsAsync(options.StaleJobThreshold, ct)
    if count > 0:
      logger.LogWarning("Released {Count} stale jobs", count)
    await Task.Delay(options.StaleJobCheckInterval, ct)

ReleaseStaleJobsAsync SQL (PostgreSQL):
  UPDATE scheduled_jobs
  SET "Status" = 'Pending',
      "LockHolder" = NULL,
      "LockedAt" = NULL,
      "NextRunTime" = now()
  WHERE "Status" = 'Running'
    AND "LockedAt" < now() - @staleness
  RETURNING count(*)
```

Default threshold: 5 minutes (configurable via `SchedulerOptions.StaleJobThreshold`).

### Execution Timeout Enforcement

Ticker used `CancellationTokenSource.CancelAfter()` per job execution. The scheduler should enforce per-job timeouts:

```csharp
// In SchedulerBackgroundService._ExecuteJobAsync:
var timeout = job.Timeout ?? _options.DefaultJobTimeout;

if (timeout.HasValue)
{
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(timeout.Value);
    await dispatcher.DispatchAsync(job, execution, timeoutCts.Token);
}
else
{
    await dispatcher.DispatchAsync(job, execution, cancellationToken);
}
```

### One-Time Job Scheduling

`ScheduledJobType.OneTime` exists but no public creation API. Add to `IScheduledJobManager`:

```csharp
Task ScheduleOnceAsync(
    string name,
    DateTimeOffset runAt,
    Type consumerType,
    string? payload = null,
    CancellationToken cancellationToken = default);
```

This creates a `ScheduledJob` with `Type=OneTime`, `NextRunTime=runAt`, no cron expression. The consumer is resolved at runtime — no pre-registration required (Hangfire/Quartz.NET pattern). `ScheduledJobDispatcher` tries keyed DI first, falls back to `ActivatorUtilities.CreateInstance`. The consumer type's assembly-qualified name is stored in job metadata for runtime resolution.

### Scheduling-Only Mode

Current `Setup.cs` registers transport-dependent processors (`IDispatcher`, `IConsumerRegister`, `MessageProcessingServer`) unconditionally. For scheduling-only mode (no transport, no `ITransport` registered), these should be conditional or gracefully degrade. The `Bootstrapper` hosted service also starts transport subscribers — it should skip when no transport is configured.

### Handler Resolution Optimization

Replace per-dispatch `GetRequiredKeyedService` lookup with a cached delegate factory. On first dispatch for a given job name, compile a `Func<IServiceProvider, IConsume<ScheduledTrigger>>` via `FastExpressionCompiler` and cache it. Subsequent dispatches use the cached factory directly.

### Misfire Strategy

When a scheduler restarts after downtime, jobs whose `NextRunTime` is far in the past need a strategy. Two options:

- `FireImmediately` (default): Execute the missed run as soon as the scheduler picks it up. This is the current implicit behavior.
- `SkipAndScheduleNext`: Skip the missed occurrence and compute the next future cron occurrence.

A job is considered "misfired" when `now - NextRunTime > MisfireThreshold`. The threshold is configurable via `SchedulerOptions.MisfireThreshold` (default: 1 minute). This prevents thundering herd when many jobs have past `NextRunTime` after a long scheduler outage.

```csharp
// In SchedulerBackgroundService._ExecuteJobAsync, before dispatch:
var misfireThreshold = _options.MisfireThreshold;
var isMisfired = (timeProvider.GetUtcNow() - job.NextRunTime!.Value) > misfireThreshold;

if (isMisfired && job.MisfireStrategy == MisfireStrategy.SkipAndScheduleNext)
{
    // Skip this execution, advance to next cron occurrence
    job.NextRunTime = cronCache.GetNextOccurrence(job.CronExpression!, job.TimeZone, timeProvider.GetUtcNow());
    job.Status = ScheduledJobStatus.Pending;
    job.LockHolder = null;
    job.LockedAt = null;
    await storage.UpdateJobAsync(job, cancellationToken);
    logger.LogWarning("Skipped misfired job '{JobName}', next run: {NextRun}", job.Name, job.NextRunTime);
    return;
}
```

### Health Checks

`SchedulerHealthCheck` implements `IHealthCheck` from `Microsoft.Extensions.Diagnostics.HealthChecks`. Reports:
- **Healthy**: Scheduler is running, storage is reachable, no stale jobs.
- **Degraded**: Stale jobs exist (count > 0) — indicates potential node failures.
- **Unhealthy**: Storage unreachable or scheduler not running.

Registered via `builder.Services.AddHealthChecks().AddSchedulerChecks()` extension method.

### Cron Override from IConfiguration

The `SchedulerJobReconciler` checks `IConfiguration` before using the `[Recurring]` attribute's cron expression. This allows ops to override schedules via `appsettings.json` or environment variables without redeployment.

```json
{
  "Messaging": {
    "Scheduling": {
      "Jobs": {
        "UsageReportJob": {
          "CronExpression": "0 0 */12 * * *"
        }
      }
    }
  }
}
```

The reconciler resolves the effective cron expression as: `IConfiguration["Messaging:Scheduling:Jobs:{Name}:CronExpression"] ?? attribute.CronExpression`. Only the cron expression is overridable from config (not timezone, retry intervals, etc.) to keep the surface small.

### InMemory Storage

`InMemoryScheduledJobStorage` in `Headless.Messaging.InMemoryStorage` using `ConcurrentDictionary<Guid, ScheduledJob>` + `ConcurrentDictionary<Guid, List<JobExecution>>`. `AcquireDueJobsAsync` uses `lock` for atomicity (single-process only). Essential for unit testing consumers without a database.

---

## Stories

### Abstractions

#### US-017: Add ReleaseStaleJobsAsync to IScheduledJobStorage [S]

Add method for recovering stuck jobs.

**Files to Study:**
- `src/Headless.Messaging.Abstractions/Scheduling/IScheduledJobStorage.cs`
- `src/Headless.Messaging.Core/Scheduling/SchedulerOptions.cs`

**Acceptance Criteria:**
- [ ] `Task<int> ReleaseStaleJobsAsync(TimeSpan staleness, CancellationToken)` added to `IScheduledJobStorage`
- [ ] Returns count of released jobs
- [ ] XML docs explain staleness threshold semantics

#### US-018: Add PurgeExecutionsAsync to IScheduledJobStorage [S]

Add method for cleaning up old execution history.

**Files to Study:**
- `src/Headless.Messaging.Abstractions/Scheduling/IScheduledJobStorage.cs`

**Acceptance Criteria:**
- [ ] `Task<int> PurgeExecutionsAsync(TimeSpan retention, CancellationToken)` added to `IScheduledJobStorage`
- [ ] Deletes execution records with `CompletedAt` older than `now - retention`
- [ ] Returns count of purged records
- [ ] XML docs explain retention semantics

#### US-019: Add Timeout support to RecurringAttribute and ScheduledJob [S]

Per-job execution timeout — Ticker had this, Messaging doesn't.

**Files to Study:**
- `src/Headless.Messaging.Abstractions/RecurringAttribute.cs`
- `src/Headless.Messaging.Abstractions/Scheduling/ScheduledJob.cs`

**Acceptance Criteria:**
- [ ] `int TimeoutSeconds` property (default 0 = no timeout) on `RecurringAttribute`
- [ ] `TimeSpan? Timeout` property on `ScheduledJob` entity
- [ ] `ScheduledJobDefinition` carries timeout from attribute to job during reconciliation
- [ ] XML docs on both properties

#### US-020: Add ScheduleOnceAsync to IScheduledJobManager [S]

Enable programmatic one-time job scheduling at runtime.

**Files to Study:**
- `src/Headless.Messaging.Abstractions/Scheduling/IScheduledJobManager.cs`
- `src/Headless.Messaging.Core/Scheduling/ScheduledJobManager.cs`

**Acceptance Criteria:**
- [ ] `Task ScheduleOnceAsync(string name, DateTimeOffset runAt, Type consumerType, string? payload, CancellationToken)` added to `IScheduledJobManager`
- [ ] Implementation creates `ScheduledJob` with `Type=OneTime`, `NextRunTime=runAt`, `Status=Pending`
- [ ] Stores consumer Type assembly-qualified name in `ScheduledJob` for runtime resolution
- [ ] `ScheduledJobDispatcher` resolves consumer: try keyed DI first, fallback to `ActivatorUtilities.CreateInstance`
- [ ] Throws if `runAt` is in the past
- [ ] XML docs

#### US-045: Add MisfireStrategy to RecurringAttribute and ScheduledJob [S]

Per-job misfire strategy — determines behavior when a job's `NextRunTime` is significantly in the past (scheduler downtime, long GC pause, etc.).

**Files to Study:**
- `src/Headless.Messaging.Abstractions/RecurringAttribute.cs`
- `src/Headless.Messaging.Abstractions/Scheduling/ScheduledJob.cs`
- `src/Headless.Messaging.Core/Scheduling/ScheduledJobDefinition.cs`
- `src/Headless.Messaging.Core/Scheduling/SchedulerOptions.cs`

**Acceptance Criteria:**
- [ ] `MisfireStrategy` enum in Abstractions: `FireImmediately` (default), `SkipAndScheduleNext`
- [ ] `MisfireStrategy MisfireStrategy` property (default `FireImmediately`) on `RecurringAttribute`
- [ ] `MisfireStrategy` property on `ScheduledJob` entity
- [ ] `ScheduledJobDefinition` carries misfire strategy from attribute to job during reconciliation
- [ ] `MisfireThreshold` (TimeSpan, default 1 minute) added to `SchedulerOptions`
- [ ] XML docs on enum, properties, and option

### Core

#### US-021: Create StaleJobRecoveryService [M]

Background service that recovers stuck jobs — equivalent to Ticker's `TickerQFallbackBackgroundService`.

**Files to Study:**
- `src/Headless.Messaging.Core/Scheduling/SchedulerBackgroundService.cs`
- `src/Headless.Messaging.Core/Scheduling/SchedulerOptions.cs`

**Acceptance Criteria:**
- [ ] `StaleJobRecoveryService` internal sealed `BackgroundService` in Core
- [ ] Polls `IScheduledJobStorage.ReleaseStaleJobsAsync` at configurable interval
- [ ] Add `StaleJobThreshold` (default 5min) and `StaleJobCheckInterval` (default 30s) to `SchedulerOptions`
- [ ] Logs warning when stale jobs are released
- [ ] Only registered when `IScheduledJobStorage` present (same guard as `SchedulerBackgroundService`)
- [ ] Graceful shutdown on cancellation

#### US-022: Enforce execution timeout in SchedulerBackgroundService [M]

Use `CancellationTokenSource.CancelAfter()` to enforce per-job timeout.

**Files to Study:**
- `src/Headless.Messaging.Core/Scheduling/SchedulerBackgroundService.cs`
- `src/Headless.Messaging.Core/Scheduling/ScheduledJobDispatcher.cs`

**Acceptance Criteria:**
- [ ] If `job.Timeout` has value, create linked `CancellationTokenSource` with `CancelAfter(timeout)`
- [ ] Add `DefaultJobTimeout` (TimeSpan?, default null = no timeout) to `SchedulerOptions`
- [ ] Fallback chain: `job.Timeout ?? options.DefaultJobTimeout ?? no timeout`
- [ ] Timed-out jobs logged as errors and marked as failed execution
- [ ] `OperationCanceledException` from timeout treated as failure, not shutdown

#### US-023: Validate scheduling-only mode [S]

Ensure `AddMessages()` works without any transport configured.

**Files to Study:**
- `src/Headless.Messaging.Core/Setup.cs`
- `src/Headless.Messaging.Core/Internal/Bootstrapper.cs`

**Acceptance Criteria:**
- [ ] `AddMessages(o => { o.UsePostgreSql(conn); o.ScanConsumers(asm); })` works without transport
- [ ] Transport-dependent processors skip gracefully when `ITransport` not registered
- [ ] `Bootstrapper` does not crash when no transport subscribers exist
- [ ] Unit tests validate code paths (integration test in US-028)

#### US-024: Optimize ScheduledJobDispatcher with compiled delegates [M]

Cache compiled handler factories instead of per-dispatch keyed DI lookup.

**Files to Study:**
- `src/Headless.Messaging.Core/Scheduling/ScheduledJobDispatcher.cs`
- `src/Headless.Messaging.Core/Internal/CompiledMessageDispatcher.cs`

**Acceptance Criteria:**
- [ ] `ConcurrentDictionary<string, Func<IServiceProvider, IConsume<ScheduledTrigger>>>` cache
- [ ] On first dispatch for a job name: compile factory via `FastExpressionCompiler`
- [ ] Subsequent dispatches use cached factory
- [ ] Fallback to keyed DI if compilation fails
- [ ] Thread-safe cache population

#### US-046: Enforce misfire strategy in SchedulerBackgroundService [S]

Check misfire condition before dispatching a job.

**Files to Study:**
- `src/Headless.Messaging.Core/Scheduling/SchedulerBackgroundService.cs`
- `src/Headless.Messaging.Core/Scheduling/SchedulerOptions.cs`

**Acceptance Criteria:**
- [ ] Before dispatch, check if `now - job.NextRunTime > options.MisfireThreshold`
- [ ] If misfired and `job.MisfireStrategy == SkipAndScheduleNext`: skip execution, compute next cron occurrence, update job, log warning
- [ ] If misfired and `FireImmediately` (default): execute normally (current behavior, no code change)
- [ ] Only applies to recurring jobs (one-time jobs always fire)
- [ ] Log includes job name and next scheduled time

#### US-047: Add scheduler health checks [S]

Health check for scheduler status, storage reachability, and stale job detection.

**Files to Study:**
- `src/Headless.Messaging.Core/Scheduling/SchedulerBackgroundService.cs`
- `src/Headless.Messaging.Core/Setup.cs`

**Acceptance Criteria:**
- [ ] `SchedulerHealthCheck` internal sealed class implementing `IHealthCheck`
- [ ] Healthy: storage reachable (lightweight query)
- [ ] Degraded: stale jobs exist (count > 0, requires `ReleaseStaleJobsAsync` method)
- [ ] Unhealthy: storage query throws
- [ ] `AddSchedulerHealthChecks()` extension on `IHealthChecksBuilder`
- [ ] Only registered when `IScheduledJobStorage` present
- [ ] Reports stale count and storage latency in health check data

#### US-048: Add cron override from IConfiguration [S]

Allow ops to override `[Recurring]` cron expressions from `appsettings.json` without redeployment.

**Files to Study:**
- `src/Headless.Messaging.Core/Scheduling/SchedulerJobReconciler.cs`
- `src/Headless.Messaging.Core/Scheduling/ScheduledJobDefinition.cs`

**Acceptance Criteria:**
- [ ] `SchedulerJobReconciler` accepts `IConfiguration` via DI
- [ ] Before using attribute cron, check `IConfiguration["Messaging:Scheduling:Jobs:{Name}:CronExpression"]`
- [ ] Config value takes precedence over attribute value
- [ ] If config value is invalid cron, log error and fall back to attribute value
- [ ] Only cron expression is overridable from config (not timezone, retry, etc.)
- [ ] XML docs explain the override mechanism

### InMemory Storage

#### US-025: Create InMemoryScheduledJobStorage [M]

In-memory implementation for unit testing and dev scenarios.

**Files to Study:**
- `src/Headless.Messaging.InMemoryStorage/InMemoryDataStorage.cs`
- `src/Headless.Messaging.InMemoryStorage/Setup.cs`

**Acceptance Criteria:**
- [ ] `InMemoryScheduledJobStorage` sealed class in `Headless.Messaging.InMemoryStorage`
- [ ] Implements all `IScheduledJobStorage` methods including `ReleaseStaleJobsAsync` and `PurgeExecutionsAsync`
- [ ] `AcquireDueJobsAsync` uses `lock` for atomicity
- [ ] `UpsertJobAsync` matches by `Name`
- [ ] `DeleteJobAsync` cascade-deletes executions
- [ ] Registered via existing `UseInMemoryStorage()` extension method
- [ ] Thread-safe via `ConcurrentDictionary` + `lock` for atomic operations

### PostgreSQL Storage Update

#### US-026: Implement new IScheduledJobStorage methods in PostgreSQL [S]

Add `ReleaseStaleJobsAsync` and `PurgeExecutionsAsync` to existing `PostgreSqlScheduledJobStorage`.

**Files to Study:**
- `src/Headless.Messaging.PostgreSql/PostgreSqlScheduledJobStorage.cs`

**Acceptance Criteria:**
- [ ] `ReleaseStaleJobsAsync`: `UPDATE ... SET Status='Pending' WHERE Status='Running' AND LockedAt < now() - @staleness`
- [ ] `PurgeExecutionsAsync`: `DELETE FROM job_executions WHERE CompletedAt < now() - @retention`
- [ ] Both use parameterized queries
- [ ] Both return affected row count

### Tests & Docs

#### US-027: Unit tests for hardening features [L]

**Files to Study:**
- `tests/Headless.Messaging.Core.Tests.Unit/`
- `tests/Headless.Messaging.Abstractions.Tests.Unit/`

**Acceptance Criteria:**
- [ ] `StaleJobRecoveryService`: polling, release count logging, configurable threshold
- [ ] Execution timeout: linked CTS, timeout-as-failure, fallback chain
- [ ] `ScheduleOnceAsync`: creation, past-date rejection, keyed DI registration
- [ ] Compiled delegate cache: first-dispatch compilation, cache hit, thread safety
- [ ] Scheduling-only mode: no transport, only storage + scheduler
- [ ] Misfire strategy: `SkipAndScheduleNext` skips misfired job, `FireImmediately` executes normally
- [ ] Health check: healthy/degraded/unhealthy states, storage reachability
- [ ] Cron config override: IConfiguration takes precedence, invalid config falls back to attribute
- [ ] NSubstitute mocks, `TestBase`, `AbortToken`, `should_*_when_*` naming

#### US-028: Integration tests for new features [M]

**Files to Study:**
- `tests/Headless.Messaging.PostgreSql.Tests.Integration/`

**Acceptance Criteria:**
- [ ] `ReleaseStaleJobsAsync` integration test: stuck job recovered after threshold
- [ ] `PurgeExecutionsAsync` integration test: old executions deleted, recent retained
- [ ] One-time job end-to-end: create → scheduler picks up → executes → completes
- [ ] Scheduling-only mode: no transport, verify job execution
- [ ] Testcontainers PostgreSQL

#### US-029: Update README files [S]

**Files to Study:**
- `src/Headless.Messaging.Abstractions/README.md`
- `src/Headless.Messaging.Core/README.md`
- `src/Headless.Messaging.InMemoryStorage/README.md`

**Acceptance Criteria:**
- [ ] Document one-time job API (`ScheduleOnceAsync`)
- [ ] Document execution timeout configuration
- [ ] Document stale job recovery configuration
- [ ] Document scheduling-only mode (no transport)
- [ ] Document InMemory storage for testing
- [ ] Document misfire strategy configuration
- [ ] Document health checks (`AddSchedulerHealthChecks`)
- [ ] Document cron override from `IConfiguration`

---

## Resolved Questions

1. **ScheduleOnceAsync consumer registration:** Type reference with runtime resolution (Hangfire/Quartz.NET pattern). No pre-registration required. Consumer resolved at execution time: try keyed DI first, fallback to `ActivatorUtilities.CreateInstance`. Stores consumer type assembly-qualified name in job metadata.

2. **Execution purge scheduling:** Manual-only — consumers call `PurgeExecutionsAsync` from their own code or a scheduled job. No automatic background purge service. Matches both TickerQ (no automatic purge) and Messaging's existing `DeleteExpiresAsync` pattern (consumer-triggered).

3. **Stale recovery for one-time jobs:** Re-queue as `Pending` (same treatment as recurring jobs). `ReleaseStaleJobsAsync` sets all stale jobs to Pending unconditionally. If the handler fails again and exhausts retries, it will be marked Failed per existing retry logic in `SchedulerBackgroundService`.

4. **Default timeout value:** `null` (no timeout) is the default. Consumers opt-in via per-job `Timeout` property or global `DefaultJobTimeout` in `SchedulerOptions`. Fallback chain: `job.Timeout → options.DefaultJobTimeout → no timeout`.

---

## References

### Internal
- `plans/2026-02-05-feat-unified-messaging-scheduling-plan.md` — Phase 1+2 plan (complete)
- `plans/unified-messaging-scheduling.prd.json` — Phase 1+2 PRD (all pass)
- `docs/merge/feat-unified-messaging-system-plan.md` — original design doc
- `docs/merge/INSTITUTIONAL-LEARNINGS-TICKER-MESSAGING-INTEGRATION.md` — gotchas
- `src/Headless.Messaging.Core/Scheduling/SchedulerBackgroundService.cs` — current scheduler
- `src/Headless.Messaging.Abstractions/Scheduling/IScheduledJobStorage.cs` — current storage interface

### Ticker References (features being ported)
- `src/Headless.Ticker.Core/Internal/Scheduling/TickerQFallbackBackgroundService.cs` — stale recovery pattern
- `src/Headless.Ticker.Core/TickerExecutionTaskHandler.cs` — timeout/cancellation patterns

### Related Plans
- `docs/plans/2026-02-07-feat-job-execution-correlation-plan.md` — Correlation (US-006–010)
- `docs/plans/2026-02-07-feat-ticker-messaging-full-merge-plan.md` — Full Merge (US-030–044)
