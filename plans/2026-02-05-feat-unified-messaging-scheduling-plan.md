# feat: Unify Messaging + Scheduling (Phase 1 & 2)

> Scope: Core scheduling abstractions + scheduling infrastructure. Source generator (Phase 3), dashboard (Phase 4), and Ticker deprecation (Phase 5) are separate follow-up plans.

## Overview

Merge `Headless.Ticker` scheduling capabilities into `Headless.Messaging` so developers use a single system for immediate messages, delayed messages, and recurring scheduled jobs.

**What changes:**
- New types in `Headless.Messaging.Abstractions`: `ScheduledTrigger`, `[Recurring]`, `IScheduledJobStorage`, entity types
- New scheduling runtime in `Headless.Messaging.Core`: `SchedulerBackgroundService`, `ScheduledJobManager`, `CronScheduleCache`
- PostgreSQL storage for `scheduled_jobs` + `job_executions` tables
- Integration with existing `IConsume<T>`, `IConsumerBuilder<T>`, and `AddMessages()` builder

**What stays the same:**
- All existing `IConsume<T>` handlers work unchanged
- `IOutboxPublisher`, `IDirectPublisher` APIs unchanged
- Transport packages (RabbitMQ, Kafka, etc.) unchanged
- Existing `messages` table schema unchanged

---

## Critical Architecture Decision: Handler Routing

Multiple classes implement `IConsume<ScheduledTrigger>` (e.g., `TokenCleanupJob`, `DailyReportJob`). Standard DI resolution (`GetRequiredService<IConsume<ScheduledTrigger>>()`) returns only one.

**Chosen approach: Keyed DI services + dedicated `IScheduledJobDispatcher`.**

1. Each `IConsume<ScheduledTrigger>` handler is registered as a **keyed service** with the job name as key
2. `SchedulerBackgroundService` uses `IScheduledJobDispatcher` (not `CompiledMessageDispatcher`) to resolve the correct handler by job name
3. `ScanConsumers()` detects `[Recurring]` attribute and **skips transport subscription** for these types -- registers them only as keyed DI services + seeds job definitions into storage
4. Fluent `.WithSchedule()` does the same: keyed registration + storage seeding

```csharp
// Registration internals (generated or manual)
services.AddKeyedScoped<IConsume<ScheduledTrigger>, TokenCleanupJob>("CleanupExpiredTokens");
services.AddKeyedScoped<IConsume<ScheduledTrigger>, DailyReportJob>("DailyReport");

// Dispatch
var handler = serviceProvider.GetRequiredKeyedService<IConsume<ScheduledTrigger>>(jobName);
await handler.Consume(context, cancellationToken);
```

**Why not reuse `CompiledMessageDispatcher`:** It resolves by message type, not by job name. Scheduled jobs all share the same message type (`ScheduledTrigger`) but route to different handlers. A separate dispatcher avoids polluting the existing dispatch path.

**`ConsumeContext<ScheduledTrigger>` field mapping:**
- `MessageId` = execution ID (GUID)
- `Topic` = job name
- `CorrelationId` = null (unless chained, then parent execution ID)
- `Headers` = empty `MessageHeader`
- `Timestamp` = scheduled time
- `Message` = populated `ScheduledTrigger` instance

---

## Technical Approach

### Architecture

```
AddMessages(m => {
    m.UsePostgreSql(conn);           // registers IDataStorage + IScheduledJobStorage
    m.UseRabbitMQ(cfg);              // registers ITransport (optional for scheduling)
    m.ScanConsumers(asm);            // discovers IConsume<T> + [Recurring] handlers
    m.Consumer<Job>()
        .WithSchedule("0 0 8 * * *") // fluent scheduling
        .Build();
})

┌─────────────────────────────────────────────────────┐
│              Headless.Messaging.Core                 │
├─────────────────────────────────────────────────────┤
│                                                      │
│  ┌──────────────────┐  ┌──────────────────────────┐ │
│  │ IOutboxPublisher  │  │ IScheduledJobDispatcher  │ │
│  │ (transactional)   │  │ (keyed DI resolution)    │ │
│  └────────┬─────────┘  └──────────┬───────────────┘ │
│           │                        │                  │
│           ▼                        ▼                  │
│  ┌──────────────┐      ┌────────────────────────┐   │
│  │ IDataStorage │      │ IScheduledJobStorage   │   │
│  │ (messages)   │      │ (jobs + executions)    │   │
│  └──────────────┘      └────────────────────────┘   │
│                                                      │
│  Background Services:                                │
│  ┌──────────────────────────────────────────────┐   │
│  │ OutboxProcessor    (existing)                 │   │
│  │ DelayedProcessor   (existing)                 │   │
│  │ SchedulerBgService (NEW - polls due jobs)     │   │
│  └──────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────┘
```

### Scheduler Execution Flow

```
1. SchedulerBackgroundService.ExecuteAsync(ct):
   loop:
     jobs = await storage.AcquireDueJobsAsync(batchSize, ct)
     for each job:
       if job.SkipIfRunning && lockProvider?.TryAcquire("messaging:job:{name}") == null:
         continue  // skip this occurrence
       try:
         execution = await storage.CreateExecutionAsync(job, ct)
         trigger = new ScheduledTrigger { ... }
         context = new ConsumeContext<ScheduledTrigger> { ... }
         handler = sp.GetRequiredKeyedService<IConsume<ScheduledTrigger>>(job.Name)
         await handler.Consume(context, ct)
         await storage.CompleteExecutionAsync(execution, success, ct)
         job.NextRunTime = cronCache.GetNextOccurrence(job.CronExpression, job.TimeZone)
         await storage.UpdateJobAsync(job, ct)
       catch:
         await storage.CompleteExecutionAsync(execution, failed, error, ct)
         // retry logic per job.RetryIntervals
       finally:
         lockProvider?.Release(...)
     await Task.Delay(pollingInterval, ct)
```

### Retry Ownership

**Scheduler owns retry for scheduled jobs.** The messaging `MessageNeedToRetryProcessor` only handles transport-delivered messages. This avoids double-retry. Retry intervals come from `[Recurring(RetryIntervals = [1000, 5000, 30000])]` or the fluent equivalent.

### Job Definition Reconciliation

On startup, `ScanConsumers` + fluent registrations produce a list of expected jobs. The scheduler reconciles with `scheduled_jobs` table:

- **New job (in code, not in DB):** Insert with computed `NextRunTime`
- **Changed cron (expression differs):** Update expression + recompute `NextRunTime`
- **Removed job (in DB, not in code):** Mark `IsEnabled = false` (soft disable, not delete)
- **Existing match:** No change

This mirrors Ticker's `MigrateDefinedCronTickers` behavior.

---

## Database Schema

### `scheduled_jobs` table

```sql
CREATE TABLE scheduled_jobs (
    "Id"                UUID PRIMARY KEY,
    "Name"              VARCHAR(200) NOT NULL UNIQUE,
    "Type"              VARCHAR(20) NOT NULL,         -- Recurring, OneTime
    "CronExpression"    VARCHAR(100),
    "TimeZone"          VARCHAR(100) NOT NULL DEFAULT 'UTC',
    "Payload"           TEXT,
    "Status"            VARCHAR(50) NOT NULL,         -- Pending, Running, Completed, Failed, Disabled
    "NextRunTime"       TIMESTAMPTZ,
    "LastRunTime"       TIMESTAMPTZ,
    "LastRunDuration"   BIGINT,                       -- Milliseconds
    "RetryCount"        INT NOT NULL DEFAULT 0,
    "RetryIntervals"    INT[],
    "SkipIfRunning"     BOOLEAN NOT NULL DEFAULT TRUE,
    "LockHolder"        VARCHAR(256),
    "LockedAt"          TIMESTAMPTZ,
    "IsEnabled"         BOOLEAN NOT NULL DEFAULT TRUE,
    "DateCreated"       TIMESTAMPTZ NOT NULL,
    "DateUpdated"       TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_jobs_next_run ON scheduled_jobs ("NextRunTime")
    WHERE "Status" = 'Pending' AND "IsEnabled" = TRUE;
CREATE INDEX idx_jobs_lock ON scheduled_jobs ("LockHolder", "LockedAt")
    WHERE "Status" = 'Running';
```

### `job_executions` table

```sql
CREATE TABLE job_executions (
    "Id"              UUID PRIMARY KEY,
    "JobId"           UUID NOT NULL REFERENCES scheduled_jobs("Id") ON DELETE CASCADE,
    "ScheduledTime"   TIMESTAMPTZ NOT NULL,
    "StartedAt"       TIMESTAMPTZ,
    "CompletedAt"     TIMESTAMPTZ,
    "Status"          VARCHAR(50) NOT NULL,         -- Pending, Running, Succeeded, Failed
    "Duration"        BIGINT,
    "RetryAttempt"    INT NOT NULL DEFAULT 1,
    "Error"           TEXT,

    INDEX idx_executions_job_status ("JobId", "Status"),
    INDEX idx_executions_scheduled ("ScheduledTime" DESC)
);
```

### Locking Strategy

Use `LockHolder`/`LockedAt` directly on `scheduled_jobs` (same as Ticker pattern). The `IResourceLockProvider` integration is optional and additive -- if registered, it provides an extra distributed lock layer for cross-service coordination. No separate `distributed_locks` table needed for Phase 1-2.

---

## Decisions on Ticker Feature Parity

| Ticker Feature | Phase 1-2 Status | Notes |
|---|---|---|
| Cron scheduling with timezone | Included | Via `[Recurring]` and fluent API |
| Distributed locking | Included | `LockHolder` on jobs + optional `IResourceLockProvider` |
| Retry with configurable intervals | Included | Per-job `RetryIntervals` |
| `SkipIfRunning` | Included | Declarative via attribute/fluent |
| Task priority (`TickerTaskPriority`) | Deferred | All jobs run at normal priority |
| Job chaining (`FluentChainTickerBuilder`) | Deferred | `ParentJobId` reserved but not implemented |
| Typed request payloads (`TickerFunctionContext<T>`) | Deferred | `ScheduledTrigger.Payload` is string; typed payloads via Phase 3 source gen |
| Imperative `SkipIfAlreadyRunning()` | Deferred | Only declarative attribute |
| `ITickerExceptionHandler` | Deferred | Use `IConsumeFilter.OnSubscribeExceptionAsync` instead |
| `RestartThrottleManager` (dynamic scheduler wake) | Deferred | Fixed polling interval in Phase 2; optimize in Phase 3 |
| Execution timeout per job | Deferred | No `Timeout` column in Phase 1-2; use lock TTL as proxy |

---

## Stories

### Phase 1: Core Scheduling Abstractions

#### US-001: Create ScheduledTrigger message type [S]

Create `ScheduledTrigger` record in `Headless.Messaging.Abstractions`.

**Files to Study:**
- `src/Headless.Messaging.Abstractions/IConsume.cs`
- `src/Headless.Messaging.Abstractions/ConsumeContext.cs`
- `src/Headless.Ticker.Abstractions/Base/TickerFunctionContext.cs`

**Acceptance Criteria:**
- [ ] `ScheduledTrigger` sealed record with `required` properties: `ScheduledTime` (DateTimeOffset), `JobName` (string), `Attempt` (int)
- [ ] Optional properties: `CronExpression` (string?), `ParentJobId` (Guid?), `Payload` (string?)
- [ ] XML documentation on all members
- [ ] File-scoped namespace `Headless.Messaging`

#### US-002: Create RecurringAttribute [S]

Create `[Recurring]` attribute in `Headless.Messaging.Abstractions`.

**Files to Study:**
- `src/Headless.Ticker.Abstractions/Base/TickerFunctionAttribute.cs`
- `src/Headless.Messaging.Abstractions/Headless.Messaging.Abstractions.csproj`

**Acceptance Criteria:**
- [ ] Sealed class targeting `AttributeTargets.Class`
- [ ] Constructor takes `string cronExpression` (6-field format)
- [ ] Properties: `Name` (string?), `TimeZone` (string?, defaults null = UTC), `RetryIntervals` (int[]?), `SkipIfRunning` (bool, default true)
- [ ] XML docs on class and all members

#### US-003: Unit tests for scheduling types [S]

**Files to Study:**
- `tests/Headless.Messaging.Abstractions.Tests.Unit/`
- `CLAUDE.md`

**Acceptance Criteria:**
- [ ] Tests in `Headless.Messaging.Abstractions.Tests.Unit` project (create if needed)
- [ ] `TestBase` inheritance, `AbortToken`, `AwesomeAssertions`
- [ ] Verify `ScheduledTrigger` required vs optional properties
- [ ] Verify `RecurringAttribute` constructor, defaults, and `AttributeUsage`
- [ ] `should_*_when_*` naming convention

### Phase 2: Scheduling Infrastructure

#### US-004: Create scheduling entity types and enums [M]

Define the entity/model types for scheduled jobs and executions.

**Files to Study:**
- `src/Headless.Ticker.Abstractions/Entities/CronTickerEntity.cs`
- `src/Headless.Ticker.Abstractions/Entities/CronTickerOccurrenceEntity.cs`
- `src/Headless.Messaging.Abstractions/Headless.Messaging.Abstractions.csproj`

**Acceptance Criteria:**
- [ ] `ScheduledJob` entity: Id (Guid), Name (string), Type (ScheduledJobType), CronExpression (string?), TimeZone (string), Payload (string?), Status (ScheduledJobStatus), NextRunTime (DateTimeOffset?), LastRunTime (DateTimeOffset?), LastRunDuration (long?), RetryCount (int), RetryIntervals (int[]?), SkipIfRunning (bool), LockHolder (string?), LockedAt (DateTimeOffset?), IsEnabled (bool), DateCreated (DateTimeOffset), DateUpdated (DateTimeOffset)
- [ ] `JobExecution` entity: Id (Guid), JobId (Guid), ScheduledTime (DateTimeOffset), StartedAt (DateTimeOffset?), CompletedAt (DateTimeOffset?), Status (JobExecutionStatus), Duration (long?), RetryAttempt (int), Error (string?)
- [ ] Enums: `ScheduledJobStatus` (Pending, Running, Completed, Failed, Disabled), `JobExecutionStatus` (Pending, Running, Succeeded, Failed), `ScheduledJobType` (Recurring, OneTime)
- [ ] All in namespace `Headless.Messaging`, sealed, `required`/`init` properties

#### US-005: Create IScheduledJobStorage abstraction [M]

**Files to Study:**
- `src/Headless.Ticker.Abstractions/Interfaces/ITickerPersistenceProvider.cs`
- `src/Headless.Messaging.Core/Persistence/IDataStorage.cs`

**Acceptance Criteria:**
- [ ] Interface in `Headless.Messaging.Abstractions` with methods: `AcquireDueJobsAsync(int batchSize, CancellationToken)`, `GetJobByNameAsync(string, CancellationToken)`, `GetAllJobsAsync(CancellationToken)`, `UpsertJobAsync(ScheduledJob, CancellationToken)`, `UpdateJobAsync(ScheduledJob, CancellationToken)`, `DeleteJobAsync(Guid, CancellationToken)`, `CreateExecutionAsync(JobExecution, CancellationToken)`, `UpdateExecutionAsync(JobExecution, CancellationToken)`, `GetExecutionsAsync(Guid jobId, int limit, CancellationToken)`
- [ ] `AcquireDueJobsAsync` atomically marks jobs as `Running` with `LockHolder` to prevent double-pickup
- [ ] XML documentation on all methods
- [ ] `CancellationToken` on all async methods

#### US-006: Port CronScheduleCache [S]

Port cron parsing + caching from Ticker to Messaging.Core.

**Files to Study:**
- `src/Headless.Ticker.Core/` (find CronScheduleCache or equivalent)
- `src/Headless.Messaging.Core/Headless.Messaging.Core.csproj`

**Acceptance Criteria:**
- [ ] `CronScheduleCache` sealed class in `Headless.Messaging.Core` (internal)
- [ ] Uses Cronos library for 6-field cron parsing
- [ ] `GetNextOccurrence(string cron, string timeZone, DateTimeOffset from)` method
- [ ] Thread-safe caching via `ConcurrentDictionary<string, CronExpression>`
- [ ] Cronos package reference added to `Directory.Packages.props`
- [ ] Unit tests for caching, timezone, and next-occurrence calculation

#### US-007: Create PostgreSQL scheduled_jobs + job_executions schema [M]

**Files to Study:**
- `src/Headless.Messaging.PostgreSql/`
- `src/Headless.Ticker.EntityFramework/Infrastructure/`

**Acceptance Criteria:**
- [ ] EF Core `IEntityTypeConfiguration<ScheduledJob>` and `IEntityTypeConfiguration<JobExecution>`
- [ ] Table names: `scheduled_jobs`, `job_executions`
- [ ] Indexes per schema design (partial indexes for PostgreSQL)
- [ ] `scheduled_jobs.Name` has unique constraint
- [ ] FK: `job_executions.JobId` -> `scheduled_jobs.Id` CASCADE
- [ ] Integration with existing PostgreSQL messaging storage registration

#### US-008: Implement PostgreSqlScheduledJobStorage [L]

**Files to Study:**
- `src/Headless.Messaging.PostgreSql/PostgreSqlDataStorage.cs`
- `src/Headless.Ticker.EntityFramework/Infrastructure/BasePersistenceProvider.cs`

**Acceptance Criteria:**
- [ ] Implements `IScheduledJobStorage`
- [ ] `AcquireDueJobsAsync` uses `SELECT ... FOR UPDATE SKIP LOCKED` pattern
- [ ] All CRUD methods implemented
- [ ] `UpsertJobAsync` handles reconciliation (insert or update by name)
- [ ] Primary constructor, sealed, `ConfigureAwait(false)`
- [ ] All async methods propagate `CancellationToken`

#### US-009: Create IScheduledJobDispatcher [M]

Dedicated dispatcher that resolves `IConsume<ScheduledTrigger>` handlers by job name using keyed DI.

**Files to Study:**
- `src/Headless.Messaging.Core/Internal/CompiledMessageDispatcher.cs`
- `src/Headless.Messaging.Abstractions/ConsumeContext.cs`

**Acceptance Criteria:**
- [ ] `IScheduledJobDispatcher` interface with `DispatchAsync(ScheduledJob job, CancellationToken ct)`
- [ ] Implementation resolves handler via `IServiceProvider.GetRequiredKeyedService<IConsume<ScheduledTrigger>>(jobName)`
- [ ] Creates proper `ConsumeContext<ScheduledTrigger>` with execution ID, job name as topic, scheduled time
- [ ] Creates DI scope per dispatch (`await using`)
- [ ] Fires `IConsumeFilter` hooks if registered (for observability consistency)
- [ ] sealed class, internal

#### US-010: Create SchedulerBackgroundService [L]

Main scheduler loop that polls for due jobs and dispatches.

**Files to Study:**
- `src/Headless.Ticker.Core/` (TickerQSchedulerBackgroundService)
- `src/Headless.Messaging.Core/Processor/`

**Acceptance Criteria:**
- [ ] Extends `BackgroundService`
- [ ] Polls `IScheduledJobStorage.AcquireDueJobsAsync` at configurable interval (default 1s)
- [ ] Dispatches each job via `IScheduledJobDispatcher`
- [ ] Records execution via `IScheduledJobStorage.CreateExecutionAsync` / `UpdateExecutionAsync`
- [ ] Computes `NextRunTime` via `CronScheduleCache` after execution
- [ ] Handles failures: logs, increments retry count, applies `RetryIntervals`
- [ ] Graceful shutdown: completes in-flight jobs
- [ ] Only starts if `IScheduledJobStorage` is registered in DI
- [ ] `ILogger` structured logging

#### US-011: Create IScheduledJobManager [M]

Public API for runtime job management.

**Files to Study:**
- `src/Headless.Ticker.Abstractions/Interfaces/Managers/ICronTickerManager.cs`
- `src/Headless.Ticker.Abstractions/Managers/TickerManager.cs`

**Acceptance Criteria:**
- [ ] `IScheduledJobManager` interface in Abstractions: `GetAllAsync`, `GetByNameAsync`, `EnableAsync(string name)`, `DisableAsync(string name)`, `TriggerAsync(string name)` (manual run), `DeleteAsync(string name)`
- [ ] `ScheduledJobManager` sealed implementation in Core
- [ ] `TriggerAsync` creates immediate execution bypassing cron schedule
- [ ] `EnableAsync`/`DisableAsync` update status and recompute `NextRunTime`
- [ ] XML documentation on interface

#### US-012: Integrate scheduler with AddMessages builder [M]

Wire scheduling into existing `AddMessages()` registration.

**Files to Study:**
- `src/Headless.Messaging.Core/Setup.cs`
- `src/Headless.Messaging.Abstractions/IMessagingBuilder.cs`
- `src/Headless.Messaging.Abstractions/IConsumerBuilder.cs`

**Acceptance Criteria:**
- [ ] `ScanConsumers()` detects `[Recurring]` on `IConsume<ScheduledTrigger>` classes
- [ ] Detected handlers registered as keyed DI services (job name = attribute `Name` ?? class name)
- [ ] Job definitions collected for startup reconciliation
- [ ] `IConsumerBuilder<T>.WithSchedule(string cron)` extension method added
- [ ] `IConsumerBuilder<T>.WithTimeZone(string iana)` extension method added
- [ ] `SchedulerBackgroundService` registered as hosted service (only when storage present)
- [ ] `IScheduledJobManager` + `IScheduledJobDispatcher` registered in DI
- [ ] On startup: reconcile collected jobs with database (upsert + soft-disable removed)
- [ ] `ScanConsumers` does NOT create transport subscription for `IConsume<ScheduledTrigger>` types

#### US-013: Add optional distributed locking [M]

Integrate `IResourceLockProvider` for additional distributed lock layer.

**Files to Study:**
- `src/Headless.DistributedLocks.Abstractions/`
- `src/Headless.DistributedLocks.Core/RegularLocks/ResourceLockProvider.cs`

**Acceptance Criteria:**
- [ ] If `IResourceLockProvider` registered: acquire `messaging:job:{name}` lock before execution
- [ ] If not registered: rely on database `SELECT FOR UPDATE SKIP LOCKED` (already in US-008)
- [ ] `SkipIfRunning=true`: skip if lock unavailable
- [ ] Lock released after execution (even on failure)
- [ ] Configurable lock timeout (default 5 minutes, overridable per job)

#### US-014: Unit tests for scheduler components [L]

**Files to Study:**
- `tests/Headless.Messaging.Core.Tests.Unit/`
- `tests/Headless.Ticker.Tests.Unit/`

**Acceptance Criteria:**
- [ ] Tests for `SchedulerBackgroundService`: polling, dispatch, failure handling, graceful shutdown
- [ ] Tests for `ScheduledJobManager`: CRUD, enable/disable, manual trigger
- [ ] Tests for `ScheduledJobDispatcher`: keyed resolution, scope creation, filter invocation
- [ ] Tests for `CronScheduleCache`: parsing, caching, timezone, DST edge cases
- [ ] Tests for job discovery in `ScanConsumers`: detects `[Recurring]`, skips transport subscription
- [ ] Tests for startup reconciliation: new jobs inserted, changed cron updated, removed jobs disabled
- [ ] NSubstitute for mocking `IScheduledJobStorage`, `IResourceLockProvider`
- [ ] `TestBase`, `AbortToken`, `should_*_when_*` naming

#### US-015: Integration tests with PostgreSQL [M]

**Files to Study:**
- `tests/Headless.Messaging.PostgreSql.Tests.Integration/`

**Acceptance Criteria:**
- [ ] Testcontainers for PostgreSQL
- [ ] End-to-end: seed recurring job -> scheduler picks up -> handler executes -> execution recorded
- [ ] Concurrent acquisition: two instances don't double-execute same job
- [ ] Enable/disable via `IScheduledJobManager`
- [ ] Manual trigger via `TriggerAsync`
- [ ] Job reconciliation on startup
- [ ] `TestBase`, `AbortToken`

#### US-016: Update README files [S]

**Files to Study:**
- `src/Headless.Messaging.Abstractions/README.md`
- `src/Headless.Messaging.Core/README.md`

**Acceptance Criteria:**
- [ ] `Headless.Messaging.Abstractions/README.md` documents `ScheduledTrigger`, `[Recurring]`, `IScheduledJobManager`, `IScheduledJobStorage`
- [ ] `Headless.Messaging.Core/README.md` documents scheduler setup, polling config, retry behavior
- [ ] Code examples for attribute-based and fluent job registration
- [ ] Example `IConsume<ScheduledTrigger>` handler

---

## Unresolved Questions

1. **One-time scheduled jobs API:** `scheduled_jobs.Type = 'OneTime'` exists in schema, but no public API to create one-time jobs is defined. Should `IScheduledJobManager.ScheduleAsync(string name, DateTimeOffset runAt)` be added? Or is `PublishDelayAsync` sufficient for delayed one-shot work?

2. **`IConsumeFilter` for scheduled jobs:** Should filters fire for scheduled trigger dispatches? The plan assumes yes (US-009), but this adds overhead. If filters are NOT desired, the `IScheduledJobDispatcher` can skip them.

3. **SqlServer + InMemory storage:** Phase 1-2 only covers PostgreSQL. Should SqlServer and InMemory `IScheduledJobStorage` implementations be added in this plan or deferred?

4. **Lock renewal for long-running jobs:** Phase 1-2 uses a fixed lock TTL. Should heartbeat-based lock renewal be added for jobs running longer than the TTL?

5. **Scheduling-only mode (no transport):** Should `AddMessages()` work without any transport configured? Current code registers transport-dependent processors unconditionally. Need to verify graceful degradation.

---

## References

### Internal
- `docs/ideas/Unified-messaging-system-plan.md` -- full design doc
- `docs/ideas/TICKER-MESSAGING-INTEGRATION-QUICKREF.md` -- integration patterns
- `docs/ideas/INSTITUTIONAL-LEARNINGS-TICKER-MESSAGING-INTEGRATION.md` -- gotchas
- `src/Headless.Messaging.Core/Setup.cs` -- existing registration
- `src/Headless.Messaging.Core/Internal/CompiledMessageDispatcher.cs` -- existing dispatcher
- `src/Headless.Ticker.Core/` -- scheduler patterns to port

### External
- [Cronos library](https://github.com/HangfireIO/Cronos) -- cron parsing
- [CAP](https://cap.dotnetcore.xyz/) -- similar .NET messaging library
- [MassTransit job consumers](https://masstransit.io/documentation/patterns/job-consumers)
