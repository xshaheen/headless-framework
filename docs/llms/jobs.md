---
domain: Jobs (Background Jobs)
packages: Jobs.Abstractions, Jobs.Core, Jobs.Dashboard, Jobs.SourceGenerator, Jobs.EntityFramework, Jobs.EntityFramework.PostgreSql, Jobs.EntityFramework.SqlServer
---

# Jobs (Background Jobs)

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Job Types](#job-types)
    - [The `[JobFunction]` Attribute and Source Generator](#the-jobfunction-attribute-and-source-generator)
    - [Typed Job Chains](#typed-job-chains)
    - [Lease Model and Sliding Renewal](#lease-model-and-sliding-renewal)
    - [Distributed Coordination and Node Identity](#distributed-coordination-and-node-identity)
    - [Commit-Coordinated Enqueue (Atomic Enqueue)](#commit-coordinated-enqueue-atomic-enqueue)
    - [Tenant Propagation](#tenant-propagation)
- [Misfire recovery](#misfire-recovery)
    - [Where the watermark starts](#where-the-watermark-starts)
    - [When a definition enters recovery](#when-a-definition-enters-recovery)
    - [Policies](#policies)
    - [When a row already stands for the instant](#when-a-row-already-stands-for-the-instant)
    - [Applying a recovery pass](#applying-a-recovery-pass)
    - [Configuring it](#configuring-it)
    - [What an executing job sees](#what-an-executing-job-sees)
    - [Schedule-interpretation drift](#schedule-interpretation-drift)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.Jobs.Abstractions](#headlessjobsabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Jobs.Core](#headlessjobscore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Middleware](#middleware)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Jobs.Dashboard](#headlessjobsdashboard)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.Jobs.SourceGenerator](#headlessjobssourcegenerator)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)
- [OpenTelemetry Instrumentation](#opentelemetry-instrumentation)
    - [Problem Solved](#problem-solved-4)
    - [Quick Start](#quick-start-4)
    - [Configuration](#configuration-4)
    - [Side Effects](#side-effects-4)
- [Headless.Jobs.EntityFramework](#headlessjobsentityframework)
    - [Problem Solved](#problem-solved-5)
    - [Key Features](#key-features-4)
    - [Design Notes](#design-notes-2)
    - [Installation](#installation-4)
    - [Quick Start](#quick-start-5)
    - [Configuration](#configuration-5)
    - [Dependencies](#dependencies-4)
    - [Side Effects](#side-effects-5)
    - [Error Handling and Retries](#error-handling-and-retries)
- [Headless.Jobs.EntityFramework.PostgreSql](#headlessjobsentityframeworkpostgresql)
    - [Problem Solved](#problem-solved-6)
    - [Key Features](#key-features-5)
    - [Design Notes](#design-notes-3)
    - [Installation](#installation-5)
    - [Quick Start](#quick-start-6)
    - [Configuration](#configuration-6)
    - [Dependencies](#dependencies-5)
    - [Side Effects](#side-effects-6)
- [Headless.Jobs.EntityFramework.SqlServer](#headlessjobsentityframeworksqlserver)
    - [Problem Solved](#problem-solved-7)
    - [Key Features](#key-features-6)
    - [Design Notes](#design-notes-4)
    - [Installation](#installation-6)
    - [Quick Start](#quick-start-7)
    - [Configuration](#configuration-7)
    - [Dependencies](#dependencies-6)
    - [Side Effects](#side-effects-7)

> High-performance background job scheduler for .NET with cron expressions, time-based scheduling, compile-time source-generated registration, and distributed coordination.

## Quick Orientation

Required packages: `Jobs.Core` + `Jobs.EntityFramework` (persistence) + `Jobs.SourceGenerator` (compile-time job registration). Add the PostgreSQL or SQL Server Jobs EF provider package for native atomic claims; otherwise the EF package uses its portable optimistic-CAS fallback.

Optional add-ons:
- `Jobs.Dashboard` — monitoring UI with authentication (basic, API key, host auth) plus live-cluster node view
- OpenTelemetry tracing ships inside `Jobs.Core` (see [OpenTelemetry Instrumentation](#opentelemetry-instrumentation)) — there is no separate instrumentation package
- `Jobs.Abstractions` — interfaces only; pulled in transitively by `Jobs.Core`; install directly only when building a library on top

Minimum wiring (in-memory storage, no persistence):

```csharp
using Polly;
using Polly.Retry;

builder.Services.AddHeadlessJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 10;
        scheduler.SchedulerTimeZone = TimeZoneInfo.Utc;
    });
});
// No app.UseJobs() required — scheduling starts via IHostedService automatically.
```

For durable persistence register a coordination provider first, then add the EF operational store:

```csharp
builder.Services.AddHeadlessCoordination(c => c.UseSqlServer(conn));
builder
    .Services.AddHeadlessJobs()
    .UseEntityFramework(ef => ef.UseJobsDbContext<JobsDbContext>(db => db.UseSqlServer(conn)));
```

Mark job methods with `[JobFunction("name")]` (or `[JobFunction("name", cronExpression: "* * * * *")]` for cron) and add `Jobs.SourceGenerator` for compile-time zero-reflection discovery.

## Agent Instructions

- Treat `(Function, ContractVersion, Request bytes)` as a durable executable contract. A schema change needs an explicit version; do not infer it from CLR names or trace IDs. Initialize storage using the [current contract mappings](../solutions/guides/jobs-versioned-contracts.md) before starting workers or writers.
- Do NOT use Hangfire or Quartz — use `Headless.Jobs` for all background jobs in this framework.
- The registration attribute is `[JobFunction]` (`JobFunctionAttribute` in `Headless.Jobs.Base`). The first positional argument is the function name; `cronExpression` is a named parameter. Add `Headless.Jobs.SourceGenerator` to the project for compile-time registration.
- Call `AddHeadlessJobs()` on `IServiceCollection`. There is no `app.UseJobs()` call — the scheduler starts automatically through `IHostedService` registered by `AddHeadlessJobs`.
- Configure every `AddJobsDiscovery(...)` assembly inside the `AddHeadlessJobs` callback. Jobs loads those assemblies before freezing the process-wide generated catalog; late generated registrations fail deterministically. Runtime services and Dashboard use an immutable configuration-resolved registry owned by each `IHost`.
- Use `Jobs.EntityFramework` for durable persistence. Without it, jobs live in memory and are lost on restart.
- Prefer `jobs.UsePostgreSql<AppDbContext>(coordination => coordination.ClusterName = "orders")` or the SQL Server sibling after registering the application context. These configure models, native claims, same-database cluster membership, and EF commit coordination. For advanced composition, configure `UsePostgreSqlClaims()` or `UseSqlServerClaims()` inside the existing `UseEntityFramework` builder. Configure only one. Omitting both deliberately keeps the portable EF optimistic-CAS claim path. The selected package also fixes the GUID ordering every EF Jobs row is keyed with — SQL Server comb, PostgreSQL UUIDv7 — so occurrence ids stay index-friendly on the paths that do not run through the native claim strategy.
- The EF store creates a cron definition at runtime by reading the backend's **current statement** clock, and only PostgreSQL and SQL Server have one. On any other EF backend `ICronJobManager.AddAsync` / `AddBatchAsync` (coordinated or not) throws `NotSupportedException`; time jobs and the unseeded `IJobPersistenceProvider.InsertCronJobsAsync(jobs, ct)` overload still work. Seed cron definitions from `[JobFunction]` attributes or position rows yourself on such a backend.
- Custom `IJobPersistenceProvider` authors must not re-derive the coalesce-recovery decision. Snapshot `CronRecoveryPlanner.GetInspectionWindow(request)`, call `CronRecoveryPlanner.CreatePlan(...)`, and apply the returned `CronRecoveryPlan` with the store's own fenced writes. See [Applying a recovery pass](#applying-a-recovery-pass).
- The application-context provider convenience methods register coordination themselves; do not register a second provider. For the advanced `UseEntityFramework` path, register `AddHeadlessCoordination(c => c.Use…(conn))` before `AddHeadlessJobs`. Without coordination, startup throws `InvalidOperationException` naming `AddHeadlessCoordination`.
- On the durable path, node identity is `node@incarnation` (store-allocated by Coordination), not `Environment.MachineName`. `SchedulerOptionsBuilder.NodeId` is only a pre-registration display fallback — it is NOT the row owner on the durable path.
- Running jobs slide their pickup lease forward on the `LeaseRenewalInterval` cadence (default ≈ `LeaseDuration / 3`), so `LeaseDuration` (default 5 min) no longer needs to exceed the longest job runtime. Keep `LeaseDuration` ≥ `FallbackIntervalChecker` to avoid spurious re-claims of rows that are claimed but not yet started.
- Set `OnNodeDeath = NodeDeathPolicy.MarkFailed` or `Skip` on non-idempotent jobs — default `Retry` will re-run the job after a node crash.
- Do NOT install a Jobs-specific cache package. Jobs cron-expression caching reuses the host's `ICache` (`Headless.Caching.InMemory`, `.Redis`, or `.Hybrid`). Without a registered `ICache`, cron expressions are read directly from the database.
- Required transactional deadlines: set `JobOptions.RequireAtomicEnlistment = true` (or the transient entity flag for manager callers). Missing/incompatible relational capability rejects before middleware; Messaging delay is not a substitute. Keyed results are provisional until outer commit, and required keyed cancellation uses the overload with `requireAtomicEnlistment: true`.
- Atomic enqueue: call `IJobScheduler` or the low-level manager inside `db.ExecuteCoordinatedTransactionAsync(...)` to commit domain writes and the job row as one unit. The facade persists through the same managers and inherits their deferred post-commit side effects. Requires a commit-coordination provider: the application-context convenience method supplies the EF adapter; advanced plain EF setup uses `AddEntityFrameworkCommitCoordination<TContext>()`, while raw ADO uses `AddPostgreSqlCommitCoordination()` / `AddSqlServerCommitCoordination()`. Cluster membership and transaction coordination remain different subsystems. The coordinated path throws on any failure; wrap in `try/catch`.
- Establish commit coordination synchronously before entering asynchronous work. The provided `ExecuteCoordinatedTransactionAsync` helpers do this correctly; once established, the scope flows across awaits inside the operation, so domain writes and message publishes may be awaited before `AddAsync`.
- Use `[JobsConstructor]` (`JobsConstructorAttribute`) on the constructor the source generator should use when a class has multiple constructors.
- Use `IJobScheduler` for routine immediate, delayed, and recurring scheduling. Typed overloads resolve generated metadata from `typeof(TArgs)`; requestless overloads require a generated `JobFunctionDescriptor` from the generated `AppJobs` catalog.
- `JobOptions` / `RecurringJobOptions` support description, durable retry count/intervals, and node-death policy; recurring options additionally accept nullable IANA `TimeZoneId`. Execution time and cron expression are method arguments. Do not add priority to scheduling options; priority remains immutable `[JobFunction]` / descriptor metadata.
- Author static conditional continuation trees with the typed `JobChain` model (it replaces the removed fluent chain builder): `JobChain.Start(payload | descriptor)`, extend node handles with `Then` (on-success) / `Catch` (on-failure), then `await scheduler.EnqueueAsync(chain.Build(), ct)`. Each node allows one `Then` and one `Catch`; chains are capped at `SchedulerOptionsBuilder.MaxChainDepth` nodes deep (default 10); `Catch` is on-failure sugar and never recovers the parent. Setting `RequireAtomicEnlistment` in any root, `Then`, or `Catch` node's existing `JobOptions` requires the whole tree to enlist in the caller transaction before middleware; default options preserve automatic routing. See [Typed Job Chains](#typed-job-chains).
- Import `Headless.Jobs` for ordinary scheduling callbacks with the singular `JobOptionsBuilder`; the plural generic `JobsOptionsBuilder<TTimeJob, TCronJob>` configures Core. Callbacks must finish synchronously. Builders support sequential reuse with copied retry arrays; `Build()` alone does not validate or accept work. Nullable setters restore inheritance, and an unset atomic assertion cannot weaken an inherited requirement.
- For multi-tenant hosts, enable Jobs tenancy through the root tenancy seam: `AddHeadlessTenancy(t => t.Jobs(jobs => jobs.PropagateTenant().RequireTenantOnEnqueue()))`. Time jobs then capture the ambient tenant at schedule time and restore it around every execution attempt. Pass `JobOptions.TenantId` to override capture, or `JobOptions.IsSystemJob = true` for a deliberate tenantless job. Cron is always system-scope — never give a cron definition a tenant; fan out explicit-tenant time jobs from application code. See [Tenant Propagation](#tenant-propagation).
- `PauseCronAsync` / `ResumeCronAsync` control one durable cron definition by ID. Pause skips pending work but preserves `InProgress`; resume schedules one strictly-future occurrence and rebases the watermark to the resume instant, so the paused interval is never replayed as missed.
- For testing, call `options.DisableBackgroundServices()` to suppress background scheduler execution.
- To use `JobsStartMode.Manual`, set `scheduler.StartMode = JobsStartMode.Manual` inside `ConfigureScheduler`.
- Managers remain supported: inject `ITimeJobManager<TTimeJob>` / `ICronJobManager<TCronJob>` for CRUD, batching, seeding, custom entities, chains, and advanced persistence workflows.

---

## Core Concepts

### Job Types

Jobs supports two first-class job types:

**Time jobs** (`TimeJobEntity`) — one-off jobs scheduled to run at a specific UTC `ExecutionTime`. Managed via `ITimeJobManager<TTimeJob>`, or composed into static conditional continuation trees with the typed [`JobChain`](#typed-job-chains) authoring model (up to `MaxChainDepth` nodes deep, default 10).

**Cron jobs** (`CronJobEntity`) — recurring jobs defined by a cron expression (`Expression` property). Each firing generates a `CronJobOccurrenceEntity` that is claimed and executed by a scheduler worker. Managed via `ICronJobManager<TCronJob>`.

Both types share `BaseJobEntity` (`Id`, `Function`, `Description`, `CreatedAt`, `UpdatedAt`) and expose `Retries`, `RetryIntervals`, and `OnNodeDeath` policy.

### The `[JobFunction]` Attribute and Source Generator

The source generator (`Headless.Jobs.SourceGenerator`) scans for `JobFunctionAttribute` (`[JobFunction]`) on methods and generates:
- A module initializer that auto-registers job delegates with the Jobs runtime before `Main` runs.
- A delegate-free `JobFunctionDescriptor` for every function, frozen by `JobFunctionProvider` into name and typed-request indexes.
- Factory delegates for every job method.
- Constructor injection code (using the `[JobsConstructor]` constructor if present, otherwise the first public constructor).

Attribute signatures (from `Headless.Jobs.Base.JobFunctionAttribute`):

```csharp
// Cron job (cronExpression is optional — omit for time/programmatic jobs)
[JobFunction("DailyReport", cronExpression: "0 0 * * *", taskPriority: JobPriority.High)]
public static Task ExecuteAsync(IServiceProvider sp, CancellationToken ct) { ... }

// Time job or named function for programmatic enqueue
[JobFunction("ProcessOrder")]
public async Task ExecuteAsync(JobFunctionContext<OrderRequest> context, CancellationToken ct) { ... }
```

The first positional argument is the durable function identity. `IJobScheduler` obtains it from the generated descriptor, while low-level manager callers set the entity `Function` directly. Priority (`JobPriority.Normal` / `High` / `Low` / `LongRunning`) and max-concurrency are optional attribute parameters.

Typed functions are indexed by both function name and exact request `Type`; requestless descriptors have `RequestType = null` and do not appear in the inverse type index. HF005 rejects duplicate function names and HF011 rejects duplicate typed request mappings in one compilation. Cross-assembly collisions fail `JobFunctionProvider.Build()` with a deterministic ordinal-sorted report rather than choosing the first initializer. The public descriptor indexes are the configuration-independent canonical catalog; Core derives one configuration-resolved runtime registry per `IHost` after all configured `AddJobsDiscovery(...)` assemblies load.

### Typed Job Chains

A **job chain** composes one root time job with conditional continuation steps into a single tree that the scheduler persists and executes atomically. Author it with the typed `JobChain` model (`Headless.Jobs.Abstractions`); it never names a handler contract — every step's identity is a generated `JobFunctionDescriptor`, resolved from the step's payload type (or supplied explicitly for a requestless step).

```csharp
using Headless.Jobs;

// processOrder → chargeCard; on success send a receipt, on failure refund. scheduler is an injected IJobScheduler.
var chain = JobChain.Start(new ProcessOrder(orderId));
var chargeCard = chain.Root.Then(new ChargeCard(orderId));   // runs when ProcessOrder succeeds
chargeCard.Then(new SendReceipt(orderId));                   // runs when ChargeCard succeeds
chargeCard.Catch(new RefundPayment(orderId));                // runs when ChargeCard fails

var rootJobId = await scheduler.EnqueueAsync(chain.Build(), ct);
```

- `JobChain.Start<TRequest>(payload, options?, executionTime?)` — or `JobChain.Start(descriptor, options?, executionTime?)` for a requestless root — returns a `JobChainBuilder`; `builder.Root` is the root node handle.
- `Then(...)` attaches the single on-success child and `Catch(...)` the single on-failure child; each returns the new child handle so a branch can be extended further. A second `Then` (or second `Catch`) on the same node throws `InvalidOperationException`.
- `Build()` freezes the tree into an immutable `JobChain`. `IJobScheduler.EnqueueAsync(JobChain, ct)` resolves every node's descriptor, enforces the depth limit, and persists the root plus its whole descendant tree in one atomic write, returning the root job's `Guid`.

**Semantics**

- `Then` persists `RunCondition.OnSuccess`; `Catch` persists `RunCondition.OnFailure`. `Catch` is pure on-failure sugar — it does **not** recover the parent. A caught parent stays `Failed`, and a failing catch step is just another failed job whose own `Then` / `Catch` continuations follow the same rules.
- Any node (including the root) may carry both a success branch and a failure branch, and only those two — there is no parallel fan-out. A **root-only chain** (no children) is valid.
- Each step carries `JobOptions` (description, retries, retry intervals, node-death policy) plus an optional explicit execution time. Priority is **not** per-step — it is generated from `[JobFunction]` metadata and stays descriptor-canonical.
- `Build()` returns an immutable value; each `EnqueueAsync` materializes fresh entities, so re-enqueueing the same built chain yields independent trees.

**Timed descendants** — a descendant given an explicit execution time becomes eligible at the **later** of (a) its parent reaching the terminal state that matches its edge and (b) its own execution time. It never starts before its parent completes, and its execution time arriving alone never runs, fails, or skips it. When the parent instead reaches a **non-matching** terminal state (for example the parent fails while the child is an `OnSuccess` step), the timed descendant is skipped together with its subtree — mirroring the non-timed skip cascade.

**Depth limit** — `SchedulerOptionsBuilder.MaxChainDepth` (default `10`) bounds the longest root-to-leaf path; on-success and on-failure edges both count. It is enforced by `EnqueueAsync` before any row is written, and the error names the configured limit. `JobChainBuilder.Build()` additionally enforces a hard structural bound of `JobChain.MaxStructuralDepth` (64), which is also the ceiling `MaxChainDepth` may be raised to — the registration guard rejects a larger value so the two limits can never contradict.

**Operational caveats**

- The whole chain executes in-process under the root's single pickup lease: the root claims and holds its descendants to the configured depth, and continuations recurse within that one lease. If the owning node crashes mid-chain after the root already completed, the running tail can be orphaned — reclaim returns a non-timed descendant to idle with no execution time and nothing re-picks it up. Per-node `OnNodeDeath` policies still apply; per-node independent pickup is deferred hardening. Treat this as a known limitation.
- Lowering `MaxChainDepth` after deeper chains were already persisted truncates runtime traversal for those chains — nodes below the new limit are no longer claimed. This is an operational caveat, not a guarded error.

The typed builder authors `OnSuccess` (`Then`) and `OnFailure` (`Catch`) continuation edges. Lower-level entity APIs expose the other `RunCondition` values.

### Lease Model and Sliding Renewal

Every claim of a job or cron-occurrence row stamps a pickup lease: `LockedUntil = now + SchedulerOptionsBuilder.LeaseDuration` (default 5 minutes). In-memory uses the injected `TimeProvider`. EF translates `DateTime.UtcNow` inside the claim statement, so lease-expiry comparison and stamping use the database UTC clock without a separate scalar query.

**Sliding lease for running jobs (#316):** before invoking user code, a job verifies that the current node still owns the row. A running job then renews its lease on the `LeaseRenewalInterval` cadence (defaults to `LeaseDuration / 3`; an explicit value must be positive and strictly less than `LeaseDuration`). On the EF storage path, renewals compare against the **database clock** (`now()`/`GETUTCDATE()`), not a node's local clock, so cross-node clock skew cannot reclaim a healthy renewing job. If a renewal affects zero rows (the row was reclaimed or its owner changed), or if the renewal cannot complete within the cadence (a hung store), the worker cancels that job's `CancellationToken` (cancel-on-loss). If the start-time check loses ownership, user code is not invoked and the row is left `InProgress` for stalled reclaim.

Consequences:
- `LeaseDuration` no longer needs to exceed the longest job runtime; a healthy long job keeps renewing.
- A job stuck `InProgress` whose lease lapses (stopped renewing) is reclaimed per its `OnNodeDeath` policy within ≈ one `LeaseDuration` — independent of node death.
- The dead-node sweep defers `InProgress` rows to the lease: a busy node's still-leased running jobs survive membership blips and are only recovered once their lease lapses.

### Distributed Coordination and Node Identity

The durable operational store (EF provider) uses `Headless.Coordination` for:
- **Node identity**: the node owner stamped on job rows is `node@incarnation` (a store-allocated incarnation ID), not `Environment.MachineName`. K8s pod-collision handling via `POD_NAME`/`POD_NAMESPACE` is configured on `Headless.Coordination`, not on `SchedulerOptionsBuilder`.
- **Dead-node recovery**: triggered by `Coordination` `NodeLeft` events plus a periodic liveness-snapshot reconcile (`DeadNodeReconcileInterval`, default 1 minute). Backend-neutral — works without Redis. Reclaim matches the dead `node@incarnation` exactly; it never touches rows owned by a restarted node's fresh incarnation.
- **Fail-stop on membership loss**: if the local node loses coordination membership, the durable scheduler stops processing rather than stamping stale owners.
- **Orphaned-owner sweep**: on the `DeadNodeReconcileInterval` cadence the fallback service also reclaims rows stamped by an owner identity absent from the liveness snapshot entirely — a superseded incarnation (never classified Dead, so the dead-node path cannot see it) or a dead identity pruned past retention. This is the only recovery path for owner-stamped `Idle`/`Queued` rows with no execution time (non-timed chain descendants) after a whole-cluster or single-node ungraceful restart.

### Commit-Coordinated Enqueue (Atomic Enqueue)

With the application-context provider convenience method (or an explicitly registered commit-coordination adapter), `ITimeJobManager.AddAsync` / `AddBatchAsync` and `ICronJobManager.AddAsync` / `AddBatchAsync` write the job row inside the caller's ambient transaction and defer dispatch, scheduler restart, notifications, and cron-cache invalidation to post-commit.

```csharp
// Capture a stable absolute deadline before any retry of the business operation.
var reminderDueAt = timeProvider.GetUtcNow().AddHours(24);
await db.ExecuteCoordinatedTransactionAsync(
    async (ctx, ct) =>
    {
        ctx.Set<Order>().Add(order);
        await ctx.SaveChangesAsync(ct);
        await bus.PublishAsync(new OrderPlaced(order.Id), ct);
        await jobScheduler.ScheduleAsync(new OrderReminderRequest(order.Id), reminderDueAt, ct);
    },
    services: requestServiceProvider,
    cancellationToken: cancellationToken
);
// Application row, durable message, and job row commit or roll back together.
```

**Footguns:**
- The ambient scope must be established synchronously; do not create a custom async factory that sets `ICurrentCommitCoordinator`. Use `ExecuteCoordinatedTransactionAsync` or a synchronous enlistment API. After enlistment, normal awaits inside the coordinated operation preserve the scope.
- Coordinated enqueues in one scope must be sequential — the scope's DB connection/transaction is not thread-safe.
- `AddAsync` / `AddBatchAsync` **throw** on failure (validation, dead/completed transaction, mis-wire). `Update` / `Delete` return `JobResult<T>` and do not throw.
- A returned entity on the coordinated path means the row was **enlisted** (commits with the transaction), not that dispatch ran. Post-commit side effects are bounded by `PostCommitDrainTimeout` (default 30s; valid range `> 0` through `5m`); timeout releases the commit thread and the fallback poll sweep recovers dispatch.
- Cluster membership (`Headless.Coordination`) and transactional enlistment (`Headless.CommitCoordination`) remain separate subsystems. `UsePostgreSql<TContext>` / `UseSqlServer<TContext>` compose both; advanced setup registers each explicitly. Configure `jobs.ConfigureJob<OrderReminderRequest>(new JobOptions { RequireAtomicEnlistment = true })` to reject calls outside a compatible transaction without repeating the option at every schedule call.

### Tenant Propagation

Time jobs carry a persisted, length-bounded `TenantId` (`BaseJobEntity.TenantId`, max `JobsTenancyOptions.TenantIdMaxLength = 200`) so multi-tenant hosts can run tenant-scoped background work. The tenant is resolved once at schedule time and restored around every execution attempt, so a job scheduled from tenant `t1` runs its handler — and each retry — under `t1`. `TenantId` is immutable through the generic update API: update payloads (dashboard edits, seeder refreshes) cannot change or clear a stored tenant. Registration, posture, and startup diagnostics are documented with the other tenancy seams in [multi-tenancy.md](multi-tenancy.md#background-jobs); enable it with:

```csharp
builder.AddHeadlessTenancy(tenancy =>
    tenancy.Jobs(jobs => jobs.PropagateTenant().RequireTenantOnEnqueue())
);
```

The schedule and execute tenancy middleware are always registered by `AddHeadlessJobs` and no-op until the seam flips `JobsTenancyOptions`. Register a real `ICurrentTenant` source (HTTP claim resolution, `AddHeadlessDbContextServices()`, or a custom implementation) before `AddHeadlessJobs` so propagation resolves a live tenant instead of the `NullCurrentTenant` fallback.

#### Schedule-Time Resolution

The schedule middleware (`TenantPropagationScheduleMiddleware`, priority `JobMiddlewarePriority.Tenancy`) resolves the tenant on the root entity before validation and before persistence, applying these rules in order:

1. **Cron is always system-scope.** A `CronJobEntity` with a non-null `TenantId` is rejected with `JobValidatorException`; a cron definition never receives ambient capture.
2. **System-job bypass.** When `IsSystemJob = true`, the job is deliberately tenantless. It is rejected with `JobValidatorException` if it also carries an explicit `TenantId` (contradiction) or if an ambient tenant is present (escalation — tenant code cannot promote itself to system scope). Otherwise `TenantId` stays null and the decision is logged. `IsSystemJob` is transient — a schedule-time authorization flag with no execution-time meaning, never persisted.
3. **Explicit tenant wins.** A supplied `TenantId` (from `JobOptions.TenantId` or the entity) is used as-is after structural validation, even when it differs from a present ambient tenant.
4. **Ambient capture.** With no explicit tenant and `PropagateTenant()` enabled, the ambient `ICurrentTenant.Id` is captured onto the row in the same atomic write. This is the only step that reads ambient state, and it never recaptures after commit.
5. **Strict rejection.** Still tenantless, not a system job, and `RequireTenantOnEnqueue()` active → rejected with `Headless.Abstractions.MissingTenantContextException`. Without strict mode, `TenantId` stays null (system scope).

**Structural validation always runs; capture and strict mode are options-gated.** Cron-scope rejection, the system-job contradictions, and blank / over-length bounds on explicitly supplied values are enforced whenever the middleware dispatches — independent of `PropagateTenant()` / `RequireTenantOnEnqueue()`. Only ambient capture and missing-tenant rejection are gated by the seam flags, which keeps tenant-to-system escalation (R7) and tenant-scoped cron (R8) unconditional. Values fail closed: a present-but-blank or over-length **ambient** tenant rejects the enqueue exactly like an over-length explicit value rather than silently downgrading the job to system scope, and the diagnostic logs only the length, never the value.

#### Execute-Time Restoration

The execute middleware (`TenantRestoreExecuteMiddleware`) opens `ICurrentTenant.Change(state.TenantId)` inside its own `InvokeAsync` frame — the frame that awaits the handler — so the `AsyncLocal` tenant flows down into the handler and is always reverted on dispose, whether the attempt succeeds, faults, or cancels. Polly re-dispatches the execute pipeline per attempt, so every retry is freshly scoped and no scope leaks between attempts. **Restoration runs whenever the row carries a persisted `TenantId`, even with `PropagateTenant()` off** — the schedule side persists an explicit `JobOptions.TenantId` (or entity `TenantId`) regardless of the flag, so an explicitly-tenanted job always runs under its tenant, never silently system-scope. `PropagateTenant()` additionally makes a null `TenantId` clear a leaked ambient so a system job runs system-scope even if a tenant leaked onto the worker; a genuinely tenant-free host (no persisted tenant, propagation off) is a pure pass-through. The same tenant is re-established around the failure callbacks (`IJobExceptionHandler`, the cancellation handler, and `OnExhausted`), which run after the handler's own scope has unwound, so tenant-aware alerting and compensating transactions are not system-scoped.

#### Chain Propagation

Time-job chains (`FluentChainJobBuilder<TTimeJob>`, up to 3 levels) resolve descendants in a `JobsManager` tree walk after the middleware returns and before persistence — the middleware only sees the `BaseJobEntity` root, while the typed `Children` live on `TimeJobEntity<TTicker>` and are unreachable from there. Each descendant follows the root's rules:

- An **unset** non-system descendant inherits the root's resolved `TenantId`.
- A descendant's **pre-set explicit** `TenantId` wins per node and is validated for blank / length.
- A descendant marked `IsSystemJob = true` follows the same escalation rule as the root — rejected when an ambient tenant is present, otherwise it stays tenantless and the decision is logged.

Without this walk, chain descendants would persist `TenantId = null` and run system-scope while the root ran tenant-scoped — a silent scope divergence.

#### Trust Model

An explicit `TenantId` is honored even when it differs from the current ambient tenant, and the mismatch logs a warning (`JobCrossTenantEnqueue` / `JobChainDescendantCrossTenant`). This lateral tenant-to-tenant path is open by default and matches the Messaging publish middleware: any in-process code already holds `ICurrentTenant.Change`, so an explicit value adds no escalation vector the process did not already have — the guard exists for accidents (a stale `TenantId` on a reused options object), not attackers. Hosts that want hard isolation opt in with `RejectCrossTenantEnqueue()` on the tenancy seam, which turns the mismatch into a `JobValidatorException`; explicit values supplied from system scope (no ambient tenant) are always honored, so cron fan-out is unaffected. The one path that is always closed is tenant-to-**system** escalation — `IsSystemJob` under an ambient tenant is rejected regardless of options.

#### Cron Fan-Out

Cron is system-scope by contract, so tenant-scoped recurring work is an application-code pattern, not a framework feature: a system-scope cron handler enumerates tenants and schedules one tenant-scoped time job per tenant with an **explicit** `JobOptions.TenantId`.

```csharp
using Headless.Jobs.Base;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;

// A tenant-scoped time job. When it runs, the execute middleware has already restored
// ICurrentTenant to the job's TenantId, so tenant-scoped services (EF global filters,
// permission cache) observe the right tenant automatically.
public sealed record TenantReportRequest(string ReportKind);

[JobFunction("GenerateTenantReport")]
public sealed class GenerateTenantReport(IReportService reports)
{
    public Task ExecuteAsync(JobFunctionContext<TenantReportRequest> context, CancellationToken ct) =>
        reports.BuildAsync(context.Request.ReportKind, ct);
}

// A system-scope cron that fans out one tenant-scoped time job per tenant.
[JobFunction("NightlyReportFanOut", cronExpression: "0 2 * * *")]
public static async Task FanOutAsync(IServiceProvider sp, CancellationToken ct)
{
    var scheduler = sp.GetRequiredService<IJobScheduler>();
    var tenants = sp.GetRequiredService<IAppTenantDirectory>(); // application-owned enumeration

    foreach (var tenantId in await tenants.ListActiveTenantIdsAsync(ct))
    {
        // Explicit TenantId is REQUIRED: the cron handler runs system-scope, so there is no
        // ambient tenant for PropagateTenant() to capture here. Relying on ambient capture
        // inside a cron handler would silently persist tenantless jobs.
        await scheduler.EnqueueAsync(
            new TenantReportRequest("nightly"),
            new JobOptions { TenantId = tenantId, Description = $"nightly-report-{tenantId}" },
            ct
        );
    }
}
```

The framework owns no tenant enumeration, `ITenantStore`, per-tenant cron rows, or per-tenant cron expressions — fan-out is application code by design (`IReportService` and `IAppTenantDirectory` above are application-owned).

## Misfire recovery

A cron definition carries a durable **schedule watermark** — the instant through which its schedule has been
reconciled — plus a **projection** of the first occurrence after it. The watermark records what was *accounted for*
rather than what was promised, so it stays true when a rule change invalidates the derived projection, and a skip
advances it without anything firing.

That record is what makes a missed occurrence detectable at all. Before it, reconciliation state lived only as an
in-memory sleep timer: a process that died mid-sleep left no trace, and on restart simply recomputed from the current
time. The occurrence was gone with nothing to notice it had ever been due.

### Where the watermark starts

A definition created at runtime through `ICronJobManager` is **positioned by the insert itself**, anchored on the
store's instant read inside the inserting transaction — single, batch, and coordinated (`AddAsync` inside an enlisted
transaction) paths alike. Creation is therefore the anchor, and any tick between creation and the first scheduler poll
belongs to that definition's missed-run policy.

Without that seed a definition arrives unpositioned and is anchored by whichever node first sees it, at *that* moment:
every tick in between disappears with no occurrence, no recovery record, and nothing to alert on. A crash before the
first poll widens the window arbitrarily.

The anchor is the store's **current statement** clock (`clock_timestamp()` on PostgreSQL, `SYSUTCDATETIME()` on SQL
Server), never a transaction-start clock. The coordinated path joins a transaction the caller already opened, and
PostgreSQL freezes `now()` at transaction start — seeding from that would position a definition before it existed and
manufacture an immediate false backlog. Definitions created through a path that leaves the position uninitialized are anchored at store time on first wake,
so creation does not manufacture a historical backlog.

### When a definition enters recovery

An instant is **pending** when it falls at or before now and the watermark has not passed it — whether or not an
occurrence row exists for it. A definition enters recovery when more than one instant is pending, or when its single
pending instant is older than that definition's grace threshold.

The grace threshold separates ordinary lateness from a genuine miss. It defaults to 60 seconds (matching Quartz) and
is resolved once at creation and persisted **per definition**, so every node evaluates the same threshold. A locally
configured value must never decide whether an instant misfired, or two nodes would disagree about the same tick.

### Policies

| Policy | Behaviour |
|---|---|
| `Coalesce` (default) | Materializes exactly **one** run for the whole unresolved missed window, reporting the first unaccounted-for missed instant as its scheduled instant. |
| `Skip` | Materializes **no** run and simply carries the watermark past the backlog. |

Both leave the watermark at the recovery instant, so a resolved backlog is never reconsidered. A schedule whose
interval is shorter than the scheduler's wake latency will legitimately re-enter recovery on the following wake —
that is the correct outcome, not a fault.

The default matches what Hangfire, Quartz, and systemd independently converged on. Bounded catch-up — replaying more
than one missed occurrence — is deliberately not offered.

Recovery never runs an instant twice and never leaves two live occurrences for one instant. An occurrence already
executing or already finished is stepped past untouched; one that has not begun executing is either repurposed as the
coalesced run or transitioned to `Skipped`.

### When a row already stands for the instant

Whether an occurrence may be created at a `(CronJobId, ExecutionTime)` pair is decided by **one** rule —
`CronOccurrenceAccounting` — shared by the claim path, occurrence materialization, and recovery, on every provider.
Two paths answering differently is exactly how a row could be stepped past by recovery and re-fired by a native claim
in the same deployment.

A row **accounts for** its instant unless it is `Skipped` carrying `CronOccurrenceDisposition.ReplacementOwed`. Stated
as that single negation the rule is total over `JobStatus` and fails closed: live rows, every terminal status, and any
status value a newer binary wrote all suppress, and no read materializes a raw status it might not recognize.

`Disposition` is a persisted column on `CronJobOccurrences` and is the rule's **sole** input. `SkippedReason` is
display text and is never matched — two producers write the identical string `"Cron definition updated"` and owe
opposite answers.

| Disposition | Written by | Effect at the instant |
|---|---|---|
| `Accounted` (default) | every newly created row, and every ordinary retirement — pause, recovery, dead-node sweep, lapsed lease, user-code skip | Suppresses. |
| `ReplacementOwed` | the startup definition reconciliation, which retires an old-expression row **without** creating a replacement | Allows re-materialization: the fire is still owed. |
| `Superseded` | a runtime schedule edit through `ICronJobManager`, which creates its own replacement | Suppresses. Re-firing would double-run every expression edit. |

A dead owner's `Skipped` row is `Accounted` deliberately. It never executed, but getting it re-run belongs to the
reclaim and recovery path; re-materializing at claim time would race that path and risk a duplicate.

Several rows may share an instant — legal, because the unique index is filtered to live rows. Any single accounting
row takes the instant, and reads report the live row first so an older terminal one cannot mask it.

The disposition is persisted explicitly so an owed replacement remains distinguishable from an accounted instant.

### Applying a recovery pass

The recovery *decision* — which instant to materialize at, which existing row to repurpose, which to step past, which
to retire, and where the resolution window ends — is one storage-agnostic unit, `CronRecoveryPlanner` in
`Headless.Jobs.Core`, and every provider consumes it. A provider snapshots the window the planner asks for
(`GetInspectionWindow`), hands those rows back (`CreatePlan`), and applies the returned `CronRecoveryPlan` as fenced
writes inside its own transaction or critical section. The planner itself reads nothing, writes nothing, and calls
back into no storage.

Splitting it that way is not tidiness. The decision used to be hand-mirrored in the relational and in-memory providers
with matching rule-ID comments and no shared code, covered on one side only by the EF harness and on the other only by
unit tests — and CI runs the unit suite alone, so a divergence would have surfaced as a comment mismatch rather than a
failing test. The planner also *consumes* the occupied-instant rule above rather than restating it: it calls
`CronOccurrenceAccounting` over rows the provider projects through the same selector materialization uses, which is
exactly the property that was measured broken before it was single-sourced.

A plan carries an ordered list of run steps plus two resolutions:

- **Run steps** walk the missed instants in schedule order. An instant already accounted for is stepped past; the
  first unaccounted-for one either repurposes a still-claimable row standing there or creates the run under the
  request's reserved identity. The list is empty under `Skip`, and also under `Coalesce` when every missed instant is
  already accounted for.
- **Two resolutions** — one for "the walk established a run", one for "it did not" — are both planned up front,
  because that answer is only known after the fenced writes have been attempted. Each names the watermark and
  projection to persist and the span of rows to retire.

A repurpose step may legitimately fail. The relational providers read the window without a lock, so the row can begin
executing before the compare-and-set lands; zero rows affected means the instant became accounted for, and the
provider continues to the next step exactly as the walk steps past an occupied instant. A create step cannot fail that
way, so it is always the last step in the list.

Each resolution names the rows to retire as a **bound**, never as identities. A saturated evaluation that *does*
establish its run inside the examined prefix resolves the whole store-time window, so its retire bound extends past
the inspected window and covers rows the snapshot never contained; the bound plus each provider's own
still-claimable predicate is the only faithful expression of that set. The mirror case is why the inspection window
is bounded at all: a saturated pass that established no run confines its resolution to the prefix it actually
examined, because an unexamined row beyond it is the next pass's only coalesce candidate and retiring it would drop
the run the backlog is still owed.

### Configuring it

```csharp
// Declared in code: seeds the definition when it is first created.
[JobFunction("reports.nightly", "0 0 2 * * *", OnMissedRun = MissedRunPolicy.Skip, MissedRunGraceSeconds = 300)]
public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
```

```csharp
// Scheduler-wide defaults for definitions that declare neither.
builder.ConfigureScheduler(scheduler =>
{
    scheduler.DefaultMissedRunPolicy = MissedRunPolicy.Coalesce;
    scheduler.DefaultMissedRunGraceSeconds = 60;
});
```

**The persisted value is the authority.** The attribute seeds a definition only when it is created and is never
reapplied during startup reconciliation, so a value later changed through `ICronJobManager.UpdateAsync` stays in force
across restarts and redeploys. That single rule is what makes an operator override self-evident without persisting a
provenance marker — and it means changing the attribute in code does **not** change an existing definition.

### What an executing job sees

```csharp
public async Task RunAsync(JobFunctionContext context, CancellationToken cancellationToken)
{
    if (context.IsRecoveryRun)
    {
        // One coalesced run stands in for EVERY occurrence missed during the outage, not just this instant.
        // Treat RecoveredFromUtc as the lower bound of the window to process.
        await ProcessSinceAsync(context.RecoveredFromUtc!.Value, cancellationToken);
        return;
    }

    await ProcessSinceAsync(context.ScheduledFor, cancellationToken);
}
```

`Lateness` reports how late the run actually started. For a recovery run it measures from the first unaccounted-for
missed instant, so it spans the unresolved part of the outage rather than the dispatch delay. It never goes negative.

### Schedule-interpretation drift

An expression and a timezone identifier can stay byte-identical while the instant they resolve to moves — a tzdata
update shifts a zone's transitions, or the cron library changes how it reads a field. Each definition therefore stores
an opaque **evaluation fingerprint** of the rules its projection was derived under; only equality is meaningful.

A background sweep rebases definitions whose fingerprint no longer matches, independently of whether their projection
is due. That independence is the point: a rule change that moves an occurrence *earlier* hides behind the stale later
projection, so a sweep keyed on due-ness would skip exactly the definitions that need it. The rebase anchors the new
projection at or after the current instant, so a tick the changed rules moved into the past is surfaced rather than
replayed as a misfire.

Startup drains one fixed high-water snapshot before scheduler pickup is enabled. That ordering is enforced by an
explicit activation barrier rather than by hosted-service registration order, so it also holds when the application
sets `HostOptions.ServicesStartConcurrently`. Deterministically invalid definitions are durably deferred with a
provider-time exponential retry (`FingerprintFailureCount` / `FingerprintRetryAfterUtc`, capped at 24h); storage,
provider, and unknown failures fail startup closed. The periodic sweep runs every `FingerprintSweepInterval`
(default 1h; rejected at setup above 24h, because it is also the *initial* delay of that capped defer backoff) in
`FingerprintSweepBatchSize` pages (default 100), drains up to 100 consecutive full pages, performs one bounded keyset
wrap, and retains its cursor when that pass bound is reached. Custom providers must implement the stale-page,
fenced-defer, and compare-and-advance SPI with the same store-time and lost-fence rules.

"Deterministically invalid" means invalid on *every* host: an undefined missed-run policy, a negative grace, an
unparseable expression, a blank timezone identifier. A timezone identifier that only the **running host** cannot
resolve is a different failure — that host's timezone database is behind, and its peers evaluate the definition fine.
Deferring it would quarantine the definition fleet-wide on one node's evidence, so instead the affected node logs it,
counts it in `CronFingerprintSweepResult.SkippedNodeLocal`, and suppresses it **in memory only**, keyed by definition
id and schedule revision. Nothing durable is written, so peers keep dispatching it; the suppression lapses when the
definition's revision moves, and disappears entirely when the process restarts with updated tzdata. Because the
suppression removes the definition's durable quarantine, the scheduler's bounded candidate read takes a resume cursor
(`CronDispatchCandidateCursor`) and pages past a page it cannot use — filtering an already-read page would let one page
of unresolvable definitions starve every healthy definition ordered behind it.

Recovery and rebase outcomes are reported through the framework's existing logging instrumentation. A missed count is
always accompanied by whether it is exact or a lower bound — a long outage on a seconds-resolution schedule stops
counting at a ceiling, and "at least 1000" calls for a different response than "exactly 1000".

## Choosing a Provider

The base EF package is the compatibility layer. Native claim packages optimize pickup without changing the scheduler contract, lease rules, descendant stamping, or fallback-window behavior.

| Provider | Use when | Avoid when | Trade-off |
|---|---|---|---|
| EF optimistic CAS | The EF database is PostgreSQL or SQL Server but contention is low, or claim SQL must stay portable | Many workers regularly race for the same due rows | Zero extra provider package, but losing workers perform failed compare-and-swap work |
| PostgreSQL atomic claims | PostgreSQL 14+ hosts contend for due work | The operational store is not PostgreSQL | `FOR UPDATE SKIP LOCKED` lets claimers select disjoint unlocked candidates in one update-and-return transaction |
| SQL Server atomic claims | SQL Server 2019+ or Azure SQL hosts contend for due work | Page-lock contention or escalation dominates and cannot be operationally addressed | `READPAST` skips row locks, but page locks can still block; `ROWLOCK` is not a guarantee |

Native selection belongs inside `UseEntityFramework`; do not add a standalone service registration. Configure exactly one native claim provider. Selecting both is rejected during registration, while selecting neither retains the CAS fallback. The selected package also declares that backend's GUID ordering once, so every EF Jobs write path — including the CAS half of the compatible pair and the shared occurrence-materialization path — keys row ids the way that backend's primary key wants.

**Backend support is narrower than "any EF provider".** Creating a cron definition at runtime needs the store's current-statement clock, which is registered for PostgreSQL (`clock_timestamp()`) and SQL Server (`SYSUTCDATETIME()`) only; on any other EF backend that path throws `NotSupportedException` rather than seeding a definition from a transaction-start clock. Time jobs, attribute-seeded cron definitions, and the unseeded `IJobPersistenceProvider.InsertCronJobsAsync(jobs, ct)` overload are unaffected, so a third backend remains usable when the application positions its own cron rows.

The PostgreSQL and SQL Server packages are EF optimization extensions, not independent persistence providers. `Jobs.EntityFramework` retains job storage, mapping definitions, recovery, the persistence contract, and provider-neutral claim transaction lifecycle primitives. Each extension owns provider-specific claim execution, including SQL, parameters, and locking behavior.

---

## Headless.Jobs.Abstractions

Contracts, entity types, manager interfaces, and execution primitives for the Jobs system.

### Problem Solved

Provides the shared contracts — `IJobScheduler`, `ITimeJobManager<TTimeJob>`, `ICronJobManager<TCronJob>`, descriptors, entity types, options, enums, exception types, and execution context — that decouple job enqueueing code from any specific Jobs persistence provider or scheduler implementation. Consumer contracts do not depend on `Jobs.EntityFramework`.

### Key Features

- **Durable contract identity**: `[JobFunction("invoice.create", ContractVersion = "schema-v2")]` and immutable `JobFunctionDescriptor.ContractVersion` declare the stored request schema. The optional descriptor-constructor version defaults to `JobContract.InitialVersion` (`"1"`). `JobContract` defines a 200 UTF-16-unit function-name bound and a 100-unit version bound; names and versions are nonblank, ordinal, and reject surrounding whitespace, controls, and invalid Unicode without normalization or truncation.
- **Jobs lineage**: `JobOptions` and `RecurringJobOptions` accept `CorrelationId` and `CausationId`. Persisted entities, `JobExecutionState`, and both execution-context forms carry contract version and Jobs-owned correlation, causation, and tenant metadata. `CronSeedDefinition` includes a trailing version (default `"1"`); consumers that deconstruct it must include that sixth member. Seeding applies that version only to newly created definitions; existing name/version/request tuples require an explicit definition edit. Ordinary time-job and cron-definition edits preserve stored correlation and causation, including when update forms omit them.
- **Occurrence snapshots**: every new cron occurrence owns `Function`, `ContractVersion`, and a copy of serialized `Request` bytes. Provider implementations must read the current persisted definition under their materialization transaction or lock, including `InsertCronJobOccurrencesAsync` with an already-populated caller tuple. Existing-row pickup, retry, and recovery retain the stored tuple.
- **Routine scheduling facade**: `IJobScheduler` resolves generated `[JobFunction]` metadata, serializes typed requests, schedules immediate, delayed, and recurring jobs without copied function strings or entity construction, and durably pauses or resumes cron definitions by ID.
- **Generated descriptors**: immutable `JobFunctionDescriptor` values expose function identity, nullable request type, cron metadata, priority, and maximum concurrency without exposing execution delegates.
- **Scheduling options**: `JobOptions` and `RecurringJobOptions` map description, durable retry count/intervals, and node-death policy; recurring options also accept a nullable IANA `TimeZoneId`. Priority remains generated function metadata.
- **Manager interfaces**: `ITimeJobManager<TTimeJob>` and `ICronJobManager<TCronJob>` with `AddAsync`, `AddBatchAsync`, `UpdateAsync`, `UpdateBatchAsync`, `DeleteAsync`, `DeleteBatchAsync`.
- **Entity types**: `TimeJobEntity` / `TimeJobEntity<TTicker>` (parent–child chains), `CronJobEntity`, `CronJobOccurrenceEntity`, and `BaseJobEntity`. New entities keep `Id`, `CreatedAt`, and `UpdatedAt` unset until a Jobs manager stamps them during `AddAsync` / `AddBatchAsync`.
- **Execution context**: `JobFunctionContext` and `JobFunctionContext<TRequest>` — exposes `Id`, `Type`, `RetryCount`, `IsDue`, `ScheduledFor`, `FunctionName`, `CronOccurrenceOperations`, and durable `RequestCancellationAsync()` for time jobs.
- **Generated execution delegate**: `JobFunctionDelegate(IServiceProvider, JobFunctionContext, CancellationToken)` keeps the cancellation token last. The generator emits this delegate shape for the runtime.
- **Attribute types**: `JobFunctionAttribute` (`[JobFunction]`) for function/cron registration; `JobsConstructorAttribute` (`[JobsConstructor]`) for custom DI injection.
- **Retry primitives**: `TimeJobEntity.Retries`, `RetryIntervals`, `RetryCount`; `CronJobEntity.Retries`, `RetryIntervals`.
- **Node-death policy**: `NodeDeathPolicy` enum (`Retry` / `MarkFailed` / `Skip`) on both entity types; propagated from `CronJobEntity` to every generated occurrence.
- **Atomic cron materialization SPI**: `MaterializeCronScheduleOccurrenceAsync` fences the expected revision and watermark, commits a new unclaimed `Idle` occurrence or recognizes an existing occurrence, and advances the schedule position in the same provider transaction. `CronScheduleMaterializationOutcome` distinguishes a lost fence, a future projection, a new row, an existing live row, and an already-terminal row.
- **Destructive time-job claim SPI**: `IJobPersistenceProvider.QueueTimeJobsAsync` yields the caller's own candidate instances — on each won row it stamps owner, lease, `Queued` status, and the refreshed concurrency token onto the passed-in entity and prunes its child tree to the descendants the claim actually leased. A candidate batch therefore belongs to exactly one claim: never share one across concurrent claimants and never reuse one, or the later claimant presents the winner's refreshed token and can re-acquire a row that node already owns. Peek again through `GetEarliestTimeJobsAsync` instead.
- **Exception types**: `JobValidatorException` (with `Errors` list for batch failures); `TerminateExecutionException` (stop without retry, optional final `JobStatus`).
- **Typed job chains**: `JobChain` / `JobChainBuilder` / `JobChainNodeBuilder` author a static conditional continuation tree of descriptor-backed steps — `Then` (on-success) and `Catch` (on-failure), one of each per node — frozen by `Build()` into an immutable `JobChain` and enqueued atomically through `IJobScheduler.EnqueueAsync(JobChain, …)`. See [Typed Job Chains](#typed-job-chains).
- **Global exception handler**: `IJobExceptionHandler` with `HandleExceptionAsync` and `HandleCanceledExceptionAsync`.
- **Job status**: `JobStatus` enum: `Idle`, `Queued`, `InProgress`, `Succeeded`, `DueDone`, `Failed`, `Cancelled`, `Skipped`.
- **Occurrence disposition**: `CronOccurrenceDisposition` enum (`Accounted` / `ReplacementOwed` / `Superseded`) and the persisted `CronJobOccurrenceEntity.Disposition` property. This is the sole input to the occupied-instant rule that decides whether an occurrence may be created at a `(CronJobId, ExecutionTime)` pair; `SkippedReason` is display text and is never read for that decision. See "When a row already stands for the instant".

### Installation

```bash
dotnet add package Headless.Jobs.Abstractions
```

Pulled in transitively by `Headless.Jobs.Core`. Install directly only when building a library that targets Jobs interfaces without depending on the Core implementation.

### Quick Start

```csharp
using Headless.Jobs.Base;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;

public sealed record OrderReminderRequest(string OrderId);

public sealed class OrderService(IJobScheduler jobs)
{
    public async Task ScheduleReminderAsync(string orderId, CancellationToken ct)
    {
        var jobId = await jobs.EnqueueAsync(
            new OrderReminderRequest(orderId),
            new JobOptions
            {
                Description = $"order-reminder-{orderId}",
                Retries = 3,
                RetryIntervals = [30, 60, 120],
            },
            ct
        );

        Console.WriteLine($"Scheduled {jobId}");
    }
}

// Mark a method for registration (requires Jobs.SourceGenerator)
[JobFunction("SendOrderReminder")]
public static Task ExecuteAsync(
    JobFunctionContext<OrderReminderRequest> context,
    CancellationToken ct)
{
    // context.Request.OrderId, context.RetryCount, and context.ScheduledFor are available.
    return Task.CompletedTask;
}
```

The generated delegate ABI uses service provider, context, then cancellation token. Handwritten registrations use the same order:

```csharp
using Headless.Jobs;

JobFunctionDelegate handler = static (serviceProvider, context, cancellationToken) =>
{
    cancellationToken.ThrowIfCancellationRequested();
    return Task.CompletedTask;
};
```

Requestless scheduling resolves a generated descriptor and passes it to the matching overload:

```csharp
var descriptor = AppJobs.Cleanup;
var jobId = await scheduler.EnqueueAsync(descriptor, cancellationToken: ct);

var delayedId = await scheduler.ScheduleAsync(
    new OrderReminderRequest(orderId),
    DateTimeOffset.UtcNow.AddHours(24),
    cancellationToken: ct
);

var recurringId = await scheduler.ScheduleRecurringAsync(
    new OrderReminderRequest(orderId),
    "0 0 * * *",
    new RecurringJobOptions { Description = "daily-reminder", TimeZoneId = "America/New_York" },
    ct
);

var pauseAccepted = await scheduler.PauseCronAsync(recurringId, ct);
var resumeAccepted = await scheduler.ResumeCronAsync(recurringId, ct);
```

Ordinary scheduling methods return the persisted entity `Guid`; recurring scheduling returns the persisted cron-definition ID. Keyed methods return `JobScheduleResult`, separating operation disposition from the observed run ID, generation, and execution state. Unknown request types or descriptor names throw `JobFunctionNotFoundException` before persistence. Duplicate function names or typed request mappings fail deterministically while `JobFunctionProvider` builds its configuration-independent canonical indexes; Core projects a separate configuration-resolved runtime registry for each `IHost`. Low-level managers remain supported for CRUD, batching, seeding, custom entity types, chains, and advanced scenarios.

Use `ScheduleKeyedAsync(new JobKey("invoice-42"), request, dueInstant, options, ct)` for a standalone one-shot deadline. The `DateTimeOffset` instant must be captured once and reused on retries. The scope is final tenant/system plus logical function name plus business key; version is intent, not identity. Keys use ordinal equality with a 200 UTF-16 limit and reject padding, controls, and invalid Unicode. `Created` writes generation 1; `Existing` observes the current same-intent run even when terminal. Different intent returns `Conflict`. `ReplaceKeyedAsync(key, observedGeneration, request, dueInstant, options, ct)` also handles rescheduling, advancing once only while pending and unclaimed; lost-response replays report `StaleGeneration`. `CancelKeyedAsync(new JobKeyScope(functionName, tenantId), key, observedGeneration, ct)` targets that generation only. Pending cancellation becomes terminal; claimed cancellation reports a cooperative `CancellationRequested`, without proving execution or external effects stopped. Missing keys return `NotFound`.

Current and historical keyed rows remain indefinitely. Ordinary manager/provider/dashboard edits, resets, retries, and hard deletion reject them; mixed deletion rejects before removing any member. No automatic cleanup worker or forget-key/rearm API exists. A `JobChain` is a static conditional continuation tree and has no keyed scheduling/control, signals, joins, waits, compensation, mutable definitions, process state, or stream coordinates.

The `v1` fingerprint uses exact durable bytes after middleware, contract version, UTC due ticks truncated to microseconds, retries/intervals, and node-death policy. Null and empty payloads differ; empty and absent retry arrays are equivalent. Display, lineage, and tracing do not participate. Callers own stable serialization. Existing rows use their recorded algorithm; unknown algorithms fail explicitly. PostgreSQL and SQL Server use transaction-owned key locks plus filtered tenant/system uniqueness. Initialize the database according to the [keyed storage guide](../solutions/guides/jobs-keyed-scheduling.md). Compatible ambient relational transactions enlist keyed writes directly. Results returned inside them have `IsProvisional = true`; only post-commit acceleration is deferred.

Transactional deadline writes use the exact captured open connection and live transaction after validating the actual configured provider/endpoint/database, including `OnConfiguring`. External connections must be unowned (`contextOwnsConnection: false`); owned external handles are rejected before the connection service is resolved. Keyed operations require savepoints before middleware, and the whole replacement kernel has a savepoint so a failed insert cannot leave only the retirement. Read-committed conflict dispositions leave the caller usable; stronger isolation may produce provider exceptions. Failed savepoint restoration or a poisoned transaction before commit requires outer rollback and a fresh unit of work. An unknown commit outcome may already be durable: reconcile the retained key and business state before recovery; do not assume rollback undid it or automatically replay it. `IsProvisional` is an immutable observation flag, not a notification that later changes. Committed rows remain authoritative if post-commit acceleration fails.


Cron control is durable and definition-specific. Pause returns `true` only when it atomically marks the definition and skips pending `Idle` / `Queued` occurrences; it never cancels `InProgress` work. Resume returns `true` only when it wins the schedule-revision fence and creates exactly one next occurrence strictly after the resume time. It never replays the paused interval — resume rebases the schedule watermark to the resume instant, so misfire recovery sees no backlog for the paused span. `TimeZoneId` accepts IANA identifiers only; null falls back to the scheduler-global timezone, while occurrence persistence remains UTC with deterministic gap/overlap handling.

Routine calls accept a positional cancellation token: `EnqueueAsync(request, ct)`, `ScheduleAsync(request, dueAt, ct)`, `ScheduleAfterAsync(request, delay, ct)`, and `ScheduleRecurringAsync(request, cron, ct)`. Options overloads require the options argument; nullable retry and node-death fields inherit configured defaults, while `Retries = 0` explicitly disables retries. Absolute facade schedules use `DateTimeOffset` and persist the same instant in UTC. Relative ordinary schedules use the injected `TimeProvider`, accept zero delay, and reject negative or overflowing delays before persistence. Keyed schedules remain absolute: capture the due instant once and reuse it when retrying the same intent.

Omit an unused cancellation token, or pass `default` or `cancellationToken: default` to select the existing token overload. Supplying `default` followed by a cancellation token selects the options overload. Use `options:` and `configure:` to make record and callback intent explicit.

Requestless jobs have generated `AppJobs` handles in the consuming assembly's namespace: `await jobs.EnqueueAsync(AppJobs.Cleanup, ct)` for `[JobFunction("Cleanup")]`. Import that namespace or qualify the catalog when several assemblies supply jobs. Handles reference the same immutable canonical descriptors used for registration; applications do not need a string dictionary lookup.

Fluent callbacks are available on the typed and requestless `EnqueueAsync`, `ScheduleAsync`, and `ScheduleAfterAsync` calls. Import `Headless.Jobs` for `JobOptionsBuilder` and `JobSchedulerExtensions`, and `Headless.Jobs.Interfaces` for `IJobScheduler`. The singular `JobOptionsBuilder` authors one options snapshot; the plural generic `JobsOptionsBuilder<TTimeJob, TCronJob>` configures the subsystem in Core.

```csharp
using Headless.Jobs;
using Headless.Jobs.Interfaces;

public sealed class JobCaller(IJobScheduler scheduler)
{
    public Task<Guid> EnqueueAsync<TRequest>(TRequest request, CancellationToken ct) =>
        scheduler.EnqueueAsync(request, options => options.WithRetries(0).WithCorrelationId("checkout"), ct);

    public Task<Guid> ScheduleCleanupAsync(JobFunctionDescriptor cleanup, CancellationToken ct) =>
        scheduler.ScheduleAfterAsync(cleanup, TimeSpan.FromMinutes(5), options => options.WithRetryIntervals(2, 5), ct);
}
```

Each callback runs synchronously once on a fresh builder, immediately before the existing options overload. Async-void callbacks are unsupported. A null receiver or `configure: null!` throws before submission; a throwing callback submits nothing. Bare `null` still selects the existing nullable options overload. Cancellation tokens and the scheduler's returned task pass through unchanged, including pre-canceled tokens; configuration still runs before delegation.

`Build()` returns the canonical `Headless.Jobs.Models.JobOptions` without validation or resolved defaults. Nullable setters accept `null` to restore inheritance; `WithRetries(0)` disables retries, `WithRetryIntervals()` replaces inherited intervals with an empty array, and `WithRetryIntervals(null)` inherits them. `RequireAtomicEnlistment()` and `AsSystemJob()` assert true and remain set across reuse; use a fresh builder to reset them. An unset per-call atomic flag cannot weaken an inherited requirement. Existing scheduling validators still check retry values, node-death policies, and tenant/system conflicts.

Builders support sequential reuse and copy retry arrays when supplied and on every build. Mutating an input, retained builder, or one result cannot change another snapshot. Returned arrays remain caller-owned and mutable; concurrent builder mutation is unsupported. Use direct records or `builder.Build() with { ... }` for advanced options; keyed, recurring, and chain conveniences are outside this fluent surface.

### Configuration

None at the abstractions layer. All configuration is done in `Headless.Jobs.Core` via `AddHeadlessJobs(options => ...)`.

### Dependencies

- `Headless.Checks`
- `Headless.CommitCoordination.Abstractions`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

### Side Effects

None.

---

## Headless.Jobs.Core

Core implementation of the Jobs scheduler: in-memory persistence provider, execution task handler, background services, bounded task scheduler, and the `AddHeadlessJobs` DI extension.

### Problem Solved

Provides reliable background job scheduling with cron expressions, delayed execution, custom task scheduling, retry logic, and bounded in-process execution without any external job scheduler dependencies (Hangfire, Quartz, etc.). The in-memory path works standalone; the durable path composes with `Jobs.EntityFramework`.

### Key Features

- **Version-checked execution**: the frozen registry remains ordinal and uniquely keyed by function name. A stored version must exactly equal the registered descriptor version before request deserialization or cached delegate invocation. A mismatch persists a `Failed` diagnostic; a missing function retains the existing release-for-another-node behavior. Descendants are processed only after the terminal write wins its ownership fence.
- **Stable causal metadata**: a root time job uses its allocated row ID as correlation unless explicitly supplied. Jobs scheduled from an executing job inherit its correlation and use that parent execution row ID as causation; chain steps use their direct parent row ID. Root cron occurrences allocate correlation from their occurrence ID unless the definition carries explicit or inherited metadata. A parent with no explicit correlation contributes its execution row ID as the causal root. Retries preserve lineage independently of `Activity` traces, and existing tenant checks still apply.
- **`AddHeadlessJobs()`**: single DI entry point; registers managers, background services, and the in-memory persistence provider.
- **`IJobScheduler` facade**: schedules typed or requestless `[JobFunction]` methods through generated descriptor indexes, maps supported options, controls cron pause/resume, and returns persisted entity IDs or locked-transition results.
- **Injected identity and app time**: managers assign persisted IDs through `IGuidGenerator` and stamp audit/scheduling time through `TimeProvider`, including every descendant in a persisted job chain.
- **Scheduler background service**: polls for due time jobs and cron occurrences on `FallbackIntervalChecker` cadence (default 30s); also driven by soft-notification signals for near-zero latency.
- **Bounded task scheduler** (`JobsTaskScheduler`): runs normal jobs as logical worker slots on the shared .NET thread pool, bounds active async executions by `MaxConcurrency` (default `Environment.ProcessorCount`), and honors `High` → `Normal` → `Low` dequeue order. `LongRunning` work receives a dedicated thread within the separate `MaxLongRunningConcurrency` budget (default: the smaller of `MaxConcurrency` and 4). Long-running admission is queued on a detached lane (capped at two parked admissions per slot), so a saturated budget never blocks the dispatch loop; an admission rejected at the cap or dropped by cancellation/shutdown is recovered by the fallback reclaim sweep when its pickup lease lapses.
- **Sliding lease renewal** (#316): jobs verify ownership immediately before user code starts, then extend `LockedUntil` on `LeaseRenewalInterval` cadence; cancel-on-loss if renewal affects zero rows or errors.
- **Shared occupied-instant rule**: `CronOccurrenceAccounting` owns the single predicate deciding whether a `(CronJobId, ExecutionTime)` pair is already taken, plus the `CronOccurrenceInstantView` projection every provider reads it through. The raw persisted status is deliberately never materialized, so a status written by a newer binary lands on the fail-closed side instead of throwing. See [When a row already stands for the instant](#when-a-row-already-stands-for-the-instant).
- **Storage-agnostic recovery planner**: `CronRecoveryPlanner` resolves the whole coalesce decision as a pure value (`CronRecoveryPlan`, `CronRecoveryWindow`, `CronRecoveryRunStep`, `CronRecoveryRunStepKind`, `CronRecoveryResolution`) that every provider — relational, in-memory, or third-party — applies with its own fenced writes. See [Applying a recovery pass](#applying-a-recovery-pass).
- **`DisableBackgroundServices()`**: suppresses background execution; only the managers are registered (useful for worker-side-only nodes and test projects).
- **Seeder API**: `UseJobsSeeder(Func<ITimeJobManager<TTimeJob>, Task>)` and `UseJobsSeeder(Func<ICronJobManager<TCronJob>, Task>)` for startup data seeding; `IgnoreSeedDefinedCronJobs()` to skip auto-seeding of attribute-defined cron jobs.
- **GZip request payloads**: `UseGZipCompression()` on `JobsOptionsBuilder` compresses serialized request bytes. Decompression is capped at 64 MiB by default; use `UseGZipCompression(maxDecompressedBytes)` only when the application deliberately supports a different bounded payload size.
- **Exception handler**: `SetExceptionHandler<THandler>()` registers an `IJobExceptionHandler` singleton.
- **Node-death policy enforcement**: claim predicate gates the lease-expiry re-claim arm on `OnNodeDeath == Retry`; clock skew cannot speculatively re-run `Skip` or `MarkFailed` jobs.
- **Startup mode**: `SchedulerOptionsBuilder.StartMode` (`JobsStartMode.Immediate` default / `JobsStartMode.Manual`).
- **Tenancy seam**: `HeadlessTenancyBuilder.Jobs(...)` (in `SetupJobsTenancy`) exposes `PropagateTenant()`, `RequireTenantOnEnqueue()`, and `RejectCrossTenantEnqueue()`. The always-registered `TenantPropagationScheduleMiddleware` and `TenantRestoreExecuteMiddleware` capture the tenant at schedule time and restore it around every execution attempt, no-opping until the seam enables `JobsTenancyOptions`. See [Tenant Propagation](#tenant-propagation).

### Design Notes

The in-memory pickup lease uses the injected `TimeProvider`. The EF operational store uses the **database clock** for acquisition, renewal, and reclaim. Claim predicates and stamps are translated into the existing SQL statement, avoiding both cross-node clock skew and a separate clock round trip.

Keyed one-shot operations run schedule middleware before hashing final intent, normalize the new `DateTimeOffset` surface to common UTC microseconds, and preserve all current/historical generations indefinitely. Generation-fenced replacement is pending/unclaimed only; claimed cancellation is cooperative. Generic update/reset/retry/delete rejects retained keyed jobs. A static conditional continuation tree (`JobChain`) has no keyed identity/control. The fingerprint and disposition contracts are described in the Abstractions section above and in the [storage guide](../solutions/guides/jobs-keyed-scheduling.md).

The scheduler's wake and restart path lives in that same store domain. Every due instant it arbitrates — a time job's execution time, a definition's persisted `NextDueUtc`, a released child's re-stamped time — is a **store** instant, because the store is what decides due-ness. Both the cron candidate read and the time-job peek report the store instant they observed (`StoreUtcNow`) on the same statement, at no extra round trip, and the scheduler derives its sleep and its planned wake from those. The node's clock enters at exactly one place: a node/store offset refreshed on every poll that reached the store, used to convert a restart request once. Mixing the two domains is a live defect, not a style point — a store-derived duration added to a node-domain deadline makes a lagging node record a 12:30 wake as 11:30, so a job enqueued for 12:05 looks *later* than the planned wake, fails to interrupt the sleep, and runs late or falls into misfire recovery.

`AddHeadlessJobs` supplies `TimeProvider.System` and a Version 7 `IGuidGenerator` only as replaceable DI defaults. Runtime services never fall back to ambient static clocks or random GUID creation. A `JobChain` therefore carries no persisted identity or time: `IJobScheduler.EnqueueAsync(JobChain, …)` maps it to an unstamped `TimeJobEntity` tree, and the manager add path assigns missing IDs, parent IDs, and one injected-clock timestamp across the complete graph before persistence. Version 7 is the *unkeyed* default and governs the in-memory path; the EF durable store resolves the GUID ordering its backend package declared instead, so persisted row ids on SQL Server are combs rather than UUIDv7 (see [Headless.Jobs.EntityFramework](#headlessjobsentityframework)).

`SchedulerOptionsBuilder.NodeId` is used as the row owner only on the in-memory single-process path (defaults to `Environment.MachineName`). On the durable path this value is overridden by `JobsOwnerIdentityAdapter` which reads the `node@incarnation` string from `Headless.Coordination`; `NodeId` becomes a pre-registration display fallback only.

Generated module initializers populate one process-wide canonical catalog. `AddHeadlessJobs` invokes the options callback first so every `AddJobsDiscovery(...)` assembly is loaded, then freezes that catalog exactly once. Repeated builds are idempotent; registrations attempted after discovery or freeze fail deterministically instead of disappearing. `JobFunctionProvider.JobFunctionDescriptors` remains the public configuration-independent descriptor lookup for requestless scheduling.

Each `IHost` receives its own immutable runtime registry projected from the canonical catalog and that host's `IConfiguration`. Cron configuration tokens are resolved only in this host-owned registry. Scheduling, execution, seeding, fallback, managers, and Dashboard operations all consume the injected registry, so multiple hosts in one process can use different configuration without resetting or replacing one another.

Jobs remain `Queued` while waiting for worker and per-function concurrency capacity. The worker performs the owned `Queued` → `InProgress` write immediately before execution, then the execution handler performs one more lease check before invoking user code. If ownership expired while queued, the worker skips the delegate instead of starting an unowned job. Because that transition must happen at admission time, each admitted job issues its own single-row claim write — a tick with N co-due functions performs N claim round trips instead of one batched write; this is the deliberate cost of the single-winner fence.

Claiming a chained time job leases its non-timed descendants down to the configured chain depth (`SchedulerOptionsBuilder.MaxChainDepth`, default 10) to the same owner while leaving their status `Idle`; each child transitions to `InProgress` only when its `RunCondition` is satisfied by the parent's terminal state. A descendant carrying its own execution time is not claimed with the parent — it becomes claimable independently at the later of the parent's matching terminal state and its own time (see [Typed Job Chains](#typed-job-chains)). Recovery keeps the retry budget crash-durable: reclaiming a **started** attempt (an `InProgress` row whose lease lapsed, under `OnNodeDeath.Retry`) increments the persisted `RetryCount` — the interrupted attempt is consumed, per the `NodeDeathPolicy.Retry` contract — while releasing a claimed-but-unstarted (`Idle`/`Queued`) row leaves the count untouched. Execution resumes from the persisted attempt, and a row whose persisted count already exceeds the budget is terminalized `Failed` (with the exhausted callback) instead of running the handler again, so a handler that reliably kills its host cannot re-run forever.

Deleting a time job deletes its whole descendant chain. The parent/child foreign key is deliberately non-cascading, so both the in-memory and EF providers resolve the subtree explicitly and delete it deepest-first (the EF provider does so inside one transaction); the returned count includes every removed descendant. Deleting a non-root node removes only that node's subtree and leaves its ancestors intact.

A typed job function's stored request is read immediately before the handler runs. A read or deserialization failure fails that attempt and is classified by the normal retry pipeline; the handler is never invoked with a default payload, and cancellation stays cancellation. `JobsRequestProvider.GetRequestAsync` therefore returns `default` only when the job genuinely stored no request.

Dashboard SignalR notifications are best-effort on the whole scheduling path: a hub failure is logged and never aborts a claim enumeration, so a dashboard or backplane outage cannot delay job dispatch. If a claim enumeration does abort for another reason, the rows already claimed in that batch are released back to `Idle` instead of waiting out their lease.

Time-job cancellation is durable and job-ID-only through `IJobScheduler.CancelAsync(jobId)` or `context.RequestCancellationAsync()`. Idle jobs become `Cancelled` atomically; queued and in-progress jobs retain their status and set `CancelRequested`. The owning execution observes the flag before user code and then on a bounded `TimeProvider` cadence. Only a cooperative exit with that execution's exact token after durable observation writes terminal `Cancelled`. Host shutdown and lease loss are distinct causes; lease loss writes no terminal status, while an uncooperative handler keeps its natural result and leaves `CancelRequested` as audit data. An unrelated `OperationCanceledException` remains a failure.

Cron pause/resume is durable and definition-specific. Pause atomically marks the definition and skips pending `Idle` / `Queued` occurrences while preserving `InProgress` work. Resume uses a schedule-revision fence so concurrent nodes create at most one occurrence strictly after the injected `TimeProvider` instant, and rebases the definition's schedule watermark to the resume instant — which is what keeps the paused interval from being replayed once misfire recovery exists. Catch-up is no longer outside this contract: see [Misfire recovery](#misfire-recovery).

Ordinary cron dispatch first commits the expected schedule position and its occurrence outcome through one persistence operation. A newly materialized occurrence is `Idle`, unowned, and unleased; only the later claim stamps `Queued`, owner, and lease using the provider's time authority. A crash after materialization therefore leaves exactly one claimable occurrence rather than an advanced position with a missing tick.

Initialize the relational Jobs database from the current EF model before starting workers or writers. Include cancellation, cron control, schedule watermarks, recovery and fingerprint-defer fields, the fingerprint retry/keyset index, and the required occurrence `Disposition` column. Custom persistence providers must supply equivalent mappings, constraints, and indexes. Custom persistence providers must implement atomic pause/resume/update plus candidate selection, atomic materialization/recovery, bounded stale-fingerprint paging, fenced defer, and compare-and-advance before use; plan the recovery half with `CronRecoveryPlanner` rather than re-deriving it. Candidate selection additionally takes a `CronDispatchCandidateCursor? after` between `limit` and the cancellation token, which must be applied inside the query — before the limit truncates — on the same `(NextDueUtc, CronJobId)` ordering the result is sorted by. Two further SPI members are store-clock contracts, not conveniences: `InsertCronJobsAsync(jobs, CronSchedulePositionSeeder, ct)` must read the store's **current statement** clock inside the inserting transaction, hand it to the seeder, persist the result, and return it (the caller arms its scheduler wake from the returned value, never from a locally recomputed projection); and `GetEarliestTimeJobsAsync` now returns `EarliestTimeJobs`, whose `StoreUtcNow` must be read in the same statement as the peek, matching `CronDispatchCandidates.StoreUtcNow`. Run the shared schedule-position and recovery conformance suites for every custom provider.

Cron expressions use `RecurringJobOptions.TimeZoneId` when present and otherwise fall back to `SchedulerTimeZone`. Only validated IANA identifiers are accepted. Occurrences remain UTC; a spring-forward occurrence inside an invalid local-time gap is shifted forward by the gap, and an ambiguous fall-back occurrence runs once at the later UTC instant (the standard-time offset).

### Installation

```bash
dotnet add package Headless.Jobs.Core
```

### Quick Start

```csharp
using Headless.Jobs.Base;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;

// 1. Register Jobs
builder.Services.AddHeadlessJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.MaxConcurrency = 10;
        scheduler.SchedulerTimeZone = TimeZoneInfo.Utc;
        scheduler.LeaseDuration = TimeSpan.FromMinutes(5);
        scheduler.FallbackIntervalChecker = TimeSpan.FromSeconds(30);
    });
    options.SetExceptionHandler<MyJobExceptionHandler>();
    options.ConfigureRetries(retry =>
    {
        retry.RetryStrategy.ShouldHandle = args =>
            ValueTask.FromResult(args.Outcome.Exception is HttpRequestException);
        retry.RetryStrategy.Delay = TimeSpan.FromSeconds(30);
        retry.RetryStrategy.BackoffType = DelayBackoffType.Exponential;
        retry.RetryStrategy.UseJitter = true;
        retry.RetryStrategy.MaxDelay = TimeSpan.FromMinutes(5);
    });
});

// 2. Define a cron job (requires Jobs.SourceGenerator)
[JobFunction("Cleanup", cronExpression: "*/5 * * * *")]
public static async Task ExecuteAsync(IServiceProvider sp, CancellationToken ct)
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Running cleanup");
    await Task.CompletedTask;
}

// 3. Define a time job with DI
[JobFunction("ProcessOrder")]
public sealed class OrderProcessor(IOrderService orders)
{
    public async Task ExecuteAsync(JobFunctionContext<OrderRequest> context, CancellationToken ct)
        => await orders.ProcessAsync(context.Request, ct);
}

// 4. Schedule through generated typed metadata.
public sealed class OrderService(IJobScheduler scheduler)
{
    public Task<Guid> ScheduleAsync(OrderRequest request, CancellationToken ct) =>
        scheduler.EnqueueAsync(request, new JobOptions { Description = "process-order" }, ct);
}

// The scheduler starts via IHostedService — no app.UseJobs() call needed.
```

Routine calls accept a positional cancellation token: `EnqueueAsync(request, ct)`, `ScheduleAsync(request, dueAt, ct)`, `ScheduleAfterAsync(request, delay, ct)`, and `ScheduleRecurringAsync(request, cron, ct)`. Options overloads require the options argument; nullable retry and node-death fields inherit configured defaults, while `Retries = 0` explicitly disables retries. Absolute facade schedules use `DateTimeOffset` and persist the same instant in UTC. Relative ordinary schedules use the injected `TimeProvider`, accept zero delay, and reject negative or overflowing delays before persistence. Keyed schedules remain absolute: capture the due instant once and reuse it when retrying the same intent.

Omit an unused cancellation token, or pass `default` or `cancellationToken: default` to select the existing token overload. Supplying `default` followed by a cancellation token selects the options overload. Use `options:` and `configure:` to make record and callback intent explicit.

Requestless jobs have generated `AppJobs` handles in the consuming assembly's namespace: `await jobs.EnqueueAsync(AppJobs.Cleanup, ct)` for `[JobFunction("Cleanup")]`. Import that namespace or qualify the catalog when several assemblies supply jobs. Handles reference the same immutable canonical descriptors used for registration; applications do not need a string dictionary lookup.

Typed facade calls resolve `typeof(TArgs)`, serialize through the configured Jobs JSON/GZip pipeline, and persist through the configured manager. Requestless calls accept a descriptor from the generated `AppJobs` catalog. Immediate, delayed, and recurring methods return the persisted time-job or cron-definition ID. Unknown or stale identities fail before serialization or persistence.

`JobOptions` and `RecurringJobOptions` expose description, durable retries/intervals, and node-death policy; recurring options also expose nullable IANA `TimeZoneId`. Execution time and cron expression remain explicit method arguments; priority remains immutable `[JobFunction]` / descriptor metadata. Managers remain public and supported for CRUD, batching, seeding, custom entities, chains, and advanced persistence workflows.

Facade calls use those managers internally, so they enlist in an established `Headless.CommitCoordination` scope and retain the same deferred post-commit dispatch/restart/notification behavior.

### Middleware

Jobs middleware uses stage-specific generic attributes. Assembly placement is global; method placement beside `[JobFunction]` targets that function without repeating its durable identity. `Schedule` middleware runs in the manager exactly once per submitted entity, before validation, persistence, or coordinated post-commit work; `Execute` middleware runs inside every retry attempt. Middleware is resolved from a bounded DI scope and must invoke `next` to accept the operation.

```csharp
[assembly: JobScheduleMiddleware<AuditScheduleMiddleware>(Priority = JobMiddlewarePriority.Early)]

// Uncommon fallback: target a descriptor generated by a referenced assembly.
[assembly: JobExecuteMiddleware<ExternalInvoiceMiddleware>(Function = "external.invoice.create")]

public static class InvoiceJobs
{
    [JobFunction("invoice.create")]
    [JobExecuteMiddleware<InvoiceExecutionMiddleware>]
    public static Task CreateAsync(JobFunctionContext<InvoiceRequest> context) => Task.CompletedTask;
}
```

Register each middleware type with DI using the lifetime it needs; the generated dispatcher resolves it from the bounded scheduling or execution scope.

```csharp
builder.Services.AddScoped<AuditScheduleMiddleware>();
builder.Services.AddScoped<ExternalInvoiceMiddleware>();
builder.Services.AddScoped<InvoiceExecutionMiddleware>();
```

The generic constraint requires schedule and execute middleware to implement their matching interfaces. Declarations are ordered by ascending priority, then middleware type identity. The generated dispatcher is direct-call/AOT-safe: runtime plugin discovery, class-handler targeting, and registration after `JobFunctionProvider.Build()` are unsupported. Include function or middleware-only assemblies in `AddJobsDiscovery` when the runtime would not otherwise load them before startup registration freezes.

### Configuration

Configure facade policies once with `ConfigureDefaults(new JobOptions { Retries = 3, RetryIntervals = [5, 30] })`, then override individual fields with `ConfigureJob<MyRequest>(new JobOptions { OnNodeDeath = NodeDeathPolicy.MarkFailed })` or `ConfigureJob(AppJobs.Cleanup, options)`. The order of precedence is call, function, then application defaults; null fields inherit and an empty interval array explicitly replaces inherited intervals. Required atomic enlistment is cumulative: `false` at a narrower level cannot disable a requirement. Configuration snapshots retry arrays and freezes per host after the registration callback; unknown generated identities and invalid retry settings fail before use/startup. Configure each function by either request type or canonical descriptor, not both.

Startup policies accept only retries, retry intervals, node-death policy, and required atomic enlistment. Tenant, system scope, description, correlation, and causation remain per-invocation metadata; supplying them in startup policies throws. Concurrency and priority remain generated function/scheduler metadata.

The plural `JobsOptionsBuilder<TTimeJob, TCronJob>` also accepts `Action<JobOptionsBuilder>` for all three policy methods. Import `Headless.Jobs` for the singular options builder and `Headless.Jobs.Enums` for `NodeDeathPolicy`. For example, use `ConfigureDefaults(job => job.WithRetries(3).WithRetryIntervals(5, 30))`, `ConfigureJob<MyRequest>(job => job.WithNodeDeathPolicy(NodeDeathPolicy.MarkFailed))`, or `ConfigureJob(AppJobs.Cleanup, job => job.WithRetries(5))`. Each callback runs once synchronously with a fresh builder; asynchronous callbacks are unsupported. Each successful call replaces the previous policy for that scope. Callback failures or invalid settings leave the prior policy intact. Retained builders and supplied arrays cannot change the captured policy or another host.

Bare `null` and `default` arguments are ambiguous between the options-record and callback configuration overloads. Use `options:` or a typed `JobOptions` argument for the record overload, and `configure:` or a typed `Action<JobOptionsBuilder>` for the callback overload. Both reject null arguments.

Policies apply to facade one-shot scheduling, keyed scheduling/replacement, every chain node, and required atomic assertions on keyed cancellation using its scope function. Facade recurring definitions inherit retries and node-death policy, but reject a required-atomic policy because that operation has no such guarantee. Attribute-seeded cron definitions and low-level manager calls retain their own settings. Ordinary ID-only cancellation and cron pause/resume retain their existing control semantics.

```csharp
builder.Services.AddHeadlessJobs(options =>
{
    options.ConfigureScheduler(scheduler =>
    {
        scheduler.NodeId = "my-node"; // in-memory path only
        scheduler.MaxConcurrency = 10; // default: processor count
        scheduler.IdleWorkerTimeOut = TimeSpan.FromMinutes(1); // default: 1 min
        scheduler.LeaseDuration = TimeSpan.FromMinutes(5); // default: 5 min
        scheduler.LeaseRenewalInterval = null; // null → LeaseDuration / 3
        scheduler.FallbackIntervalChecker = TimeSpan.FromSeconds(30); // default: 30s
        scheduler.PostCommitDrainTimeout = TimeSpan.FromSeconds(30); // default: 30s; > 0, max: 5 min
        scheduler.SchedulerTimeZone = TimeZoneInfo.Utc; // default: UTC — never Local (fleet-divergent cron dedup)
        scheduler.DeadNodeReconcileInterval = TimeSpan.FromMinutes(1); // durable path; default: 1 min
        scheduler.StartMode = JobsStartMode.Immediate; // or Manual
        scheduler.MaxChainDepth = 10; // default: 10; range 1..JobChain.MaxStructuralDepth (64)
    });

    options.SetExceptionHandler<MyJobExceptionHandler>();
    options.DisableBackgroundServices(); // test / enqueue-only nodes
    options.UseGZipCompression(); // compress request payloads
    options.IgnoreSeedDefinedCronJobs(); // skip auto-seeding of attribute cron jobs
    options.UseJobsSeeder(async manager => // startup time-job seeder
    {
        await manager.AddAsync(new TimeJobEntity { Function = "Init", ExecutionTime = DateTime.UtcNow });
    });
    options.ConfigureRequestJsonOptions(json =>
    {
        json.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });
});
```

### Dependencies

- `Headless.Jobs.Abstractions`
- `Headless.Coordination.Abstractions`
- `Headless.Coordination.Core`
- `Headless.DistributedLocks.Abstractions`
- `Headless.MultiTenancy`
- `Headless.Extensions`
- `NCrontab.Signed`
- `Polly.Core`

### Side Effects

- Registers `ITimeJobManager<TimeJobEntity>` and `ICronJobManager<CronJobEntity>` as singletons.
- Registers one non-generic `IJobScheduler` facade bound to the same configured time/cron entity pair.
- Registers background hosted services: `JobsInitializationHostedService` (always), `JobsSchedulerBackgroundService`, `JobsFallbackBackgroundService`, and `JobsExecutionTaskHandler` (unless `DisableBackgroundServices()` is called).
- Registers `JobsTaskScheduler` (shared-thread-pool logical workers bounded by active async `MaxConcurrency`; dedicated threads only for `LongRunning`).
- Registers a per-host `CronScheduleCache` (scheduler timezone) and the per-host `JobsRequestSerializationOptions` singleton (request JSON options, GZip, decompression cap) consumed by `JobsHelper` — no process-global serializer state.
- Registers the Jobs tenancy primitives: `TenantPropagationScheduleMiddleware` / `TenantRestoreExecuteMiddleware` (`TryAddSingleton`), an `AsyncLocal`-backed `ICurrentTenantAccessor`, and the `ICurrentTenant` fallback (`NullCurrentTenant`, replaced by a real `CurrentTenant` once an HTTP / EF / consumer seam registers one). Inserts the schedule and execute tenancy middleware into the process-global registry once per process at `JobMiddlewarePriority.Tenancy`; both no-op until the tenancy seam enables `JobsTenancyOptions`.

---

## Headless.Jobs.Dashboard

Embedded web monitoring UI for `Headless.Jobs` with pluggable authentication and real-time cluster updates.

### Problem Solved

Provides operational visibility into the Jobs scheduler — job queues, execution history, live cluster nodes, retry/failure details — without requiring a separate monitoring service. The dashboard is embedded in the host application and mounted under a configurable URL path.

### Key Features

- **Contract and lineage visibility**: function descriptors expose `ContractVersion`; time-job, cron-definition, and occurrence views show version, correlation, causation, and tenant fields. Occurrences show their own snapshotted function. Add/update forms submit the selected descriptor version, and request inspection reports an unsupported stored version before attempting deserialization. Live occurrence updates retain the same metadata.
- **Embedded SPA**: served from the host process, no separate deployment.
- **Authentication options**: `WithBasicAuth(username, password)`, `WithApiKey(apiKey)`, `WithHostAuthentication(policy?)` (delegates to host app's auth), or explicit no-auth mode for isolated development dashboards.
- **Safe host-auth handoff**: fragment-delivered access tokens are removed from the URL, then validated only after the SPA initializes the host authentication configuration.
- **Predictable timestamp display**: explicit ISO UTC offsets are preserved, zone-less values are treated as UTC, and invalid values render empty instead of `NaN`.
- **Responsive operational layout**: content cards shrink within mobile viewports while wide data tables retain their own overflow boundary.
- **Live cluster view**: `GET /api/nodes` returns live node projections from `Headless.Coordination` membership; `NodeJoined` / `NodeLeft` / `NodeSuspected` push updates over SignalR — no polling required.
- **Error monitoring**: surfaces failed, cancelled, and skipped jobs; retry counts; execution timings; exception messages.
- **Storage-reduced cron graphs**: bundled providers select distinct UTC dates and aggregate status counts in storage;
  the dashboard does not load a cron job's lifetime occurrence entities to render its bounded history graph.
- **Fluent builder**: `SetBasePath(path)`, `SetBackendDomain(domain)`, `SetCorsOrigins(origins)`, `SetCorsPolicy(policy)`.
- **Pair with OpenTelemetry**: Dashboard for operational triage; the built-in OpenTelemetry instrumentation for trace-level diagnostics.

### Design Notes

The dashboard exposes operational endpoints that can create, update, delete, run, cancel, start, stop, and restart jobs. Authentication must be chosen explicitly — if no auth method (including `WithNoAuth()`) is called, the host fails to start, so the dashboard never ships publicly by omission. Treat `WithNoAuth()` as development-only unless the dashboard is isolated behind trusted network controls; production deployments should use `WithHostAuthentication(...)`, `WithBasicAuth(...)`, or `WithApiKey(...)`. No CORS policy is applied by default (same-origin only); use `SetCorsOrigins(...)` when the SPA is served cross-origin.

Cron-occurrence graph selection remains history-derived: it first chooses the same inclusive UTC date window from
distinct occurrence dates, then zero-fills gaps. `IJobPersistenceProvider.GetCronOccurrenceGraphStatusCountsAsync`
is additive and has a compatibility implementation for third-party providers. A custom durable provider should
override it so distinct-date selection and date/status aggregation happen in storage; otherwise the default
implementation preserves behavior by projecting through the existing occurrence-list API.

Dashboard API inputs are bounded: paginated queries accept page sizes from 1 through 100, JSON request bodies are limited to 1 MiB, and batch deletion accepts at most 500 IDs. Collection endpoints use the paginated routes.

### Installation

```bash
dotnet add package Headless.Jobs.Dashboard
```

### Quick Start

```csharp
using Headless.Jobs;

builder
    .Services.AddHeadlessJobs()
    .AddDashboard(dashboard =>
    {
        dashboard.SetBasePath("/jobs-dashboard");
        dashboard.WithHostAuthentication(); // or WithBasicAuth / WithApiKey
    });

// No app.MapJobs() or app.UseJobs() — the dashboard middleware is injected via IStartupFilter.
var app = builder.Build();
app.Run();
```

### Configuration

```csharp
builder
    .Services.AddHeadlessJobs()
    .AddDashboard(dashboard =>
    {
        // Path and domain
        dashboard.SetBasePath("/jobs");
        dashboard.SetBackendDomain("https://api.example.com");
        dashboard.SetCorsOrigins("https://admin.example.com"); // needed only when the SPA is cross-origin

        // Authentication — required, pick one:
        dashboard.WithBasicAuth("admin", "secret"); // username/password
        dashboard.WithApiKey("my-api-key"); // Bearer token / query param
        dashboard.WithHostAuthentication(); // delegate to host auth
        dashboard.WithHostAuthentication("AdminPolicy"); // host auth + policy
        // Or opt out explicitly with dashboard.WithNoAuth() — isolated development environments only.
    });
```

Auth detection is automatic: explicit `WithNoAuth()` → public; basic auth → username/password login UI; API key → bearer token; host auth → delegates to the host's authentication middleware.

### Dependencies

- `Headless.Jobs.Abstractions`
- `Headless.Jobs.Core`
- `Headless.Dashboard.Authentication` (shared with `Headless.Messaging.Dashboard`)
- `Headless.Extensions`

### Side Effects

- Mounts dashboard HTTP API and SignalR hub under `SetBasePath` path via `IStartupFilter` (no explicit `app.Use…` call needed).
- Subscribes to `Headless.Coordination` membership events for live-node push updates.
- Serves embedded frontend SPA assets; requires Node 22 on `PATH` when building from source (build target `make dashboards`).
- Exposes mutating operational endpoints; configure authentication and CORS before exposing the dashboard outside an isolated development environment.

---

## Headless.Jobs.SourceGenerator

Roslyn incremental source generator that eliminates reflection and manual job registration for the Jobs scheduler.

### Problem Solved

Without the source generator, every job class or method must be manually registered with the Jobs runtime at startup, and job dispatch uses reflection to invoke methods. The source generator scans for `[JobFunction]` attributes at compile time and emits a module initializer that auto-registers all discovered jobs before `Main` runs, with zero reflection at runtime.

### Key Features

- **Versioned descriptors**: `JobFunctionAttribute.ContractVersion` (default `"1"`) is emitted into assembly metadata and immutable runtime descriptors. Explicit function name and version remain stable through CLR class/method renames and source/reference reordering. Duplicate function names remain invalid even when their versions differ; versioning does not create a second dispatch registry.
- **Zero reflection**: all dispatch delegates are generated as strongly-typed lambdas.
- **Auto-registration**: a `[ModuleInitializer]` in the generated file (`JobsInstanceFactory.g.cs`) registers job delegates before any host startup code runs.
- **Descriptor indexes**: generates delegate-free descriptors for every typed and requestless function; the provider exposes frozen indexes by name and by typed request `Type`.
- **Type safety**: compile-time validation of job method signatures and cron expression syntax.
- **DI constructor injection**: generates constructor factory methods; uses `[JobsConstructor]` constructor when present, otherwise the first public constructor.
- **Incremental**: only re-generates when marked methods change (fast on large solutions).
- **Collision safety**: HF005 rejects duplicate function names and HF011 rejects duplicate typed request mappings within a compilation. Provider construction reports cross-assembly conflicts deterministically.
- **Rich diagnostics**: compile-time errors for unknown function names, ambiguous constructors, invalid cron expressions, mismatched context types, and ambiguous scheduling identities.

### Installation

```bash
dotnet add package Headless.Jobs.SourceGenerator
```

### Quick Start

```csharp
using Headless.Jobs.Base;
using Headless.Jobs.Enums;

// Static cron job (no DI)
[JobFunction("Cleanup", cronExpression: "*/5 * * * *")]
public static async Task ExecuteAsync(IServiceProvider sp, CancellationToken ct)
{
    sp.GetRequiredService<ILogger<Program>>().LogInformation("Cleaning up");
    await Task.CompletedTask;
}

// Instance job with primary constructor DI
[JobFunction("ProcessOrder")]
public sealed class OrderProcessor(IOrderService orders)
{
    public async Task ExecuteAsync(JobFunctionContext<OrderRequest> context, CancellationToken ct)
        => await orders.ProcessAsync(context.Request, ct);
}

// Multiple constructors — mark the target with [JobsConstructor]
public sealed class ComplexJob
{
    [JobsConstructor]
    public ComplexJob(ILogger<ComplexJob> logger, IConfiguration config) { ... }

    public ComplexJob() { } // default ctor ignored by generator

    [JobFunction("ComplexTask")]
    public async Task ExecuteAsync(CancellationToken ct) { ... }
}

// High-priority cron
[JobFunction("DailyReport", cronExpression: "0 0 * * *", taskPriority: JobPriority.High)]
public static Task ExecuteAsync(IServiceProvider sp, CancellationToken ct) => Task.CompletedTask;
```

### Configuration

The generator also emits a public static `AppJobs` catalog for requestless functions in the same assembly namespace as its registration class. Each getter returns the immutable canonical descriptor used during module registration. Alphanumeric contract names preserve their spelling (`Cleanup`); keywords are escaped (`@class`). Underscores, punctuation, non-ASCII characters, leading digits, and the first character of reserved member names are encoded as `_uXXXX_` UTF-16 code units. For example, `invoice.send` becomes `AppJobs.invoice_u002E_send`; literal underscores are encoded too, preventing escape-lookalike collisions. The catalog is sorted by ordinal contract name and is independent of CLR handler names and source ordering.

No runtime configuration. Attributes are the sole interface. Generated output file: `JobsInstanceFactory.g.cs` (a `[ModuleInitializer]` in the consuming assembly).

`[JobFunction]` remains the sole handler discovery model. Requestless descriptors use `RequestType = null`; typed functions are indexed by both durable function name and exact request `Type`. Attribute priority and maximum concurrency remain descriptor metadata, not per-schedule options.

### Dependencies

- `Microsoft.CodeAnalysis.CSharp` (build-time Roslyn API; not a runtime dependency)

### Side Effects

Emits `JobsInstanceFactory.g.cs` at compile time. The generated file:
- Contains a `[ModuleInitializer]` that registers job delegates, request-type mappings, and delegate-free descriptors with the Jobs runtime.
- Contains constructor factory lambdas for each discovered job class.
- Has no effect at runtime beyond the one-time module initializer invocation.

---

## OpenTelemetry Instrumentation

OpenTelemetry instrumentation for `Headless.Jobs` is built into `Headless.Jobs.Core` — activity tracing for the full job execution lifecycle plus structured logging. (The former `Headless.Jobs.OpenTelemetry` satellite package was folded into `Jobs.Core` per the framework OTel conventions; native emission needs no separate package.) Cross-cutting naming, PII, and registration rules for all Headless instrumentation live in [OpenTelemetry instrumentation conventions](../solutions/conventions/opentelemetry-instrumentation-conventions.md).

### Problem Solved

Provides distributed tracing (OpenTelemetry activities/spans) and structured log events for every Jobs job execution without modifying job code. The default `IJobsInstrumentation` emits activities natively — subscribing the tracing pipeline is the single opt-in, matching Caching/DistributedLocks/Messaging (no implementation swap step).

### Quick Start

```csharp
using OpenTelemetry.Trace;

builder.Services.AddHeadlessJobs(); // instrumentation is built in — no extra call

// Subscribe the tracing pipeline to the Jobs ActivitySource; subscribing IS the opt-in.
builder
    .Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddJobsInstrumentation() // typed helper; equivalent to AddSource(JobsDiagnostics.SourceName)
            .AddConsoleExporter(); // or Jaeger, OTLP, Azure Monitor, etc.
    });
```

### Configuration

Execution spans include `headless.job.contract_version`, `headless.job.correlation_id`, `headless.job.causation_id`, and `headless.job.tenant_id` when present. These fields preserve business lineage independently of trace ancestry and are not metric dimensions.

No activation switch exists — the default instrumentation starts activities only when a listener is subscribed (`ActivitySource` short-circuits to null otherwise), so an unobserved host pays effectively nothing and keeps identical log output. The `ActivitySource` name is `JobsDiagnostics.SourceName` (`"Headless.Jobs"`, a `public const` so dashboards and wiring reference the symbol). Subscribe with `AddJobsInstrumentation()` or `AddSource(JobsDiagnostics.SourceName)`. Custom instrumentation remains possible by registering your own `IJobsInstrumentation` after `AddHeadlessJobs()`. (Breaking vs earlier previews: `AddOpenTelemetryInstrumentation()` was removed — delete the call; spans now flow from the subscription alone.)

Span names: the per-execution root activity (display name = the job function name), plus `job.enqueue`, `job.complete`, `job.fail`, `job.cancel`, `job.skip`, `job.deserialize.fail`, `seeding.start`, `seeding.complete`.

Activity tag reference (framework-owned tags are namespaced `headless.job.*` / `headless.seeding.*` with `snake_case` segments; exception tags follow the OTel `exception.*` convention):

| Tag | Example |
|-----|---------|
| `headless.job.id` | `123e4567-…` |
| `headless.job.type` | `TimeJob`, `CronJob` |
| `headless.job.function` | `ProcessOrder` |
| `headless.job.priority` | `Normal`, `High`, `Low`, `LongRunning` |
| `headless.job.machine` | `web-01` |
| `headless.job.parent_id` | parent job GUID |
| `headless.job.run_condition` | child-job run condition (child time jobs only) |
| `headless.job.enqueued_from` | `OrderController.Create (Program.cs:42)` |
| `headless.job.retry_count` | `3` |
| `headless.job.duration_ms` | `1250` |
| `headless.job.success` | `true` / `false` |
| `headless.job.cancellation_reason` | cancel reason on `job.cancel` spans |
| `headless.job.skip_reason` | skip reason on `job.skip` spans |
| `headless.seeding.type` | seeding data type on `seeding.*` spans |
| `headless.seeding.environment` | instance identifier on `seeding.*` spans |
| `exception.type` | `SqlException` |
| `exception.message` | `Connection timeout` |
| `exception.stacktrace` | stack trace on `job.fail` spans |

### Side Effects

Registers `OpenTelemetryInstrumentation` as the singleton `IJobsInstrumentation`, replacing the default `LoggerInstrumentation`. No other registrations.

---

## Headless.Jobs.EntityFramework

Entity Framework Core persistence provider for `Headless.Jobs` — durable, distributed, multi-node job storage with database-clock lease authority.

### Problem Solved

Provides persistence of time jobs and cron occurrences across restarts and across multiple nodes, using EF Core-mapped tables. Integrates with `Headless.Coordination` for distributed node identity (`node@incarnation`), dead-node recovery, and fail-stop on membership loss.

### Key Features

- **Durable contract tuples**: time jobs and cron definitions map required bounded `Function`/`ContractVersion` columns; occurrences additionally persist their own function, version, request bytes, correlation, causation, and nullable tenant. Newly materialized occurrences copy the current definition tuple while holding its write lock; retries and restart reads use the occurrence row. Runtime write converters reject invalid identities.
- **Application-owned schema**: initialize the Jobs database from the current EF model before starting workers or definition writers. Required bounded contract columns, occurrence-owned tuples, constraints, and indexes are part of that initial schema. Library mappings never mutate the schema automatically.
- **Durable storage**: persists `TimeJobEntity`, `CronJobEntity`, and `CronJobOccurrenceEntity` in EF Core-mapped tables (default schema: `jobs`).
- **`UseEntityFramework(ef => …)`**: the EF registration extension on `JobsOptionsBuilder`.
- **`UseJobsDbContext<TDbContext>(dbOptions, schema?)`**: registers a dedicated `JobsDbContext` with configurable schema.
- **`UseApplicationDbContext<TDbContext>(ConfigurationType)`**: shares an existing application `DbContext` instead of a dedicated one.
- **Consumer-managed keyed models**: with `ConfigurationType.IgnoreModelCustomizer`, explicitly configure PostgreSQL `C` or SQL Server `Latin1_General_100_BIN2` collation on time-job `Function`, `TenantId`, and `BusinessKey` through `TimeJobConfigurations<TTimeJob>(schema, contractCollation)` or the matching model default. Keyed operations reject missing/different collation configuration without changing consumer mappings; initialize the database from the same model. Ordinary and coordinated add/update paths reject attachment to any retained keyed parent, including ORM-populated detached entities.
- **Database-clock lease authority**: on the EF path, lease renewal comparisons (`LockedUntil`) use the database server clock (`now()`/`GETUTCDATE()`), not the node's `TimeProvider`. Cross-node clock skew cannot reclaim a healthy renewing job.
- **Atomic cron materialization**: one transaction locks the expected schedule position, recognizes or inserts the exact unclaimed `Idle` occurrence, and advances the watermark only with that durable outcome. Claiming and database-clock lease stamping happen afterward.
- **Atomic chain claims**: a root time-job claim leases its non-timed descendants down to the configured chain depth (`SchedulerOptionsBuilder.MaxChainDepth`, default 10) to the same owner — atomically via a recursive CTE on the native PostgreSQL / SQL Server providers, and via a sequenced frontier walk on the EF CAS fallback where each descendant copies the root's exact lease deadline, a partial claim is pruned to the set actually claimed, and an unexecuted claimed root is recovered by the stalled-lease sweep. Fallback recovery uses the same tree claim and never steals a live queued lease.
- **Bounded compatibility recovery**: EF CAS fallback orders overdue roots by execution time and ID and processes at
  most 100 candidates per sweep, matching the native provider claim ceiling while retaining each row's CAS fence.
- **Backend-keyed row identity**: the installed native claim package declares its backend's GUID ordering once, and every EF write path resolves that keyed `IGuidGenerator` — the native strategy, the CAS half of the compatible pair, and the shared occurrence-materialization path alike. Generic EF (no backend package) registers no key and keeps the unkeyed Version 7 default.
- **Store-clock schedule seeding**: creating a cron definition at runtime positions it in the same transaction, anchored on the store's current-statement clock. Registered for PostgreSQL and SQL Server; other EF backends throw `NotSupportedException` on that path.
- **Durable retry state**: root jobs, descendants, and cron occurrences retain their persisted `RetryCount` when projected for execution.
- **Node identity and recovery**: stamps `node@incarnation` as the row owner; dead-node reclaim driven by `NodeLeft` events plus periodic reconcile (`DeadNodeReconcileInterval`).
- **Fail-fast coordination check**: startup throws `InvalidOperationException` when no coordination provider is registered.
- **Cron-expression caching**: reuses the host's `ICache` (optional). No `ICache` → reads from DB, cache invalidation is skipped. Cache failures are fail-open.
- **DbContext pool**: configurable via `SetDbContextPoolSize(n)` (default 1024).
- **Custom schema**: `SetSchema("custom_schema")` or the `schema` parameter on `UseJobsDbContext`.

### Design Notes

Lease acquisition, renewal, and reclaim on the EF path anchor `LockedUntil` to the **database clock** (`now()` on PostgreSQL, `GETUTCDATE()` on SQL Server), not the node's injected `TimeProvider`. Claims translate the clock expression inside the existing update statement; they do not execute a separate scalar query. In-memory has no database server and uses `TimeProvider`, so EF tests must not assume fake application time controls lease deadlines.

Seeding a definition's schedule position is the one EF write that cannot use that translated clock. It runs inside a transaction — the caller's own, on the coordinated path — and PostgreSQL resolves the translated `DateTime.UtcNow` to `now()`, which is frozen at transaction start. The seed therefore reads the **current statement** clock (`clock_timestamp()` / `SYSUTCDATETIME()`) on the inserting connection. Backend detection is by EF provider name rather than by which Headless backend package is installed, because generic EF (CAS claiming, no backend package) runs against those same two databases and needs the same anchor. A backend with no known statement-clock function throws `NotSupportedException` rather than seeding from a transaction-start clock — deliberately loud, because a false anchor manufactures an immediate backlog for that definition's missed-run policy to resolve, and there is no portable substitute. `ICronJobManager.AddAsync` / `AddBatchAsync` is the affected path; the unseeded `InsertCronJobsAsync(jobs, ct)` overload still works there for callers that position their own rows, and attribute-seeded definitions are anchored by the activation gate instead.

The scheduler's due-work peek (`GetEarliestTimeJobsAsync`) runs both of its reads through the context's execution strategy, so a SQL Server deadlock victim (1205) on the candidate read is retried when the application configured `EnableRetryOnFailure`. This deliberately honors whatever strategy the consumer configured instead of adding an always-on retry: it is a pass-through under EF's default non-retrying strategy, which is the right trade for a pure read whose failure costs one delayed poll. The claim path keeps its own deadlock pipeline, because a deadlock there is correctness-relevant rather than a missed poll.

Cron materialization uses a read-committed transaction whose first statement is the fenced definition update. That write lock is the per-definition mutex held through occurrence-key arbitration and commit, so concurrent nodes converge on one occurrence without serializable-transaction aborts. Every materialization writer must participate in this mutex.

The occurrence table carries the persisted `Disposition` column that `CronOccurrenceAccounting` reads as the sole input to the occupied-instant rule (see [When a row already stands for the instant](#when-a-row-already-stands-for-the-instant)). Fresh occurrence rows default to `Accounted`; definition reconciliation explicitly marks a retired occurrence `ReplacementOwed` when its fire is still owed.

The `JobsDbContext<TTimeJob, TCronJob>.DbContextOptions` constructor must be `public` for the EF pool to resolve it at startup. Validation fails fast at DI build time.

Install `Headless.Jobs.EntityFramework.PostgreSql` or `Headless.Jobs.EntityFramework.SqlServer` and select it inside the same `UseEntityFramework` builder to replace the CAS pickup path with a provider-native atomic claim-and-return operation. The scheduler and persistence contract remain database-agnostic. Register exactly one native claim provider; selecting both fails during registration.

These packages are EF optimization extensions, not standalone persistence providers. The base package owns the full persistence contract plus provider-neutral mapping definitions and claim-transaction lifecycle primitives; each extension owns provider-specific claim execution, including SQL, parameters, and locking semantics.

### Installation

```bash
dotnet add package Headless.Jobs.EntityFramework
```

### Quick Start

```csharp
using Headless.Jobs.DbContextFactory;
using Microsoft.EntityFrameworkCore;

var conn = builder.Configuration.GetConnectionString("DefaultConnection");

// 1. Register Coordination FIRST (supplies node@incarnation identity + NodeLeft recovery)
builder.Services.AddHeadlessCoordination(c => c.UseSqlServer(conn));

// 2. Register Jobs with the durable operational store
builder
    .Services.AddHeadlessJobs(options =>
    {
        options.ConfigureScheduler(scheduler => scheduler.SchedulerTimeZone = TimeZoneInfo.Utc);
    })
    .UseEntityFramework(ef =>
    {
        ef.UseJobsDbContext<JobsDbContext>(db => db.UseSqlServer(conn));
    });

// Optional: cron-expression caching via ICache
builder.Services.AddHeadlessCaching(setup =>
    setup.UseRedis(o => o.ConnectionMultiplexer = ConnectionMultiplexer.Connect("localhost:6379"))
);
```

Without a registered coordination provider the durable path throws at startup:
```
InvalidOperationException: The durable Jobs operational store requires a coordination provider.
Register one with AddHeadlessCoordination(...) before AddHeadlessJobs(... UseEntityFramework(...)).
```

### Configuration

```csharp
builder
    .Services.AddHeadlessJobs(options =>
    {
        options.ConfigureScheduler(scheduler =>
        {
            // How often the durable path reconciles dead nodes to catch missed NodeLeft signals.
            scheduler.DeadNodeReconcileInterval = TimeSpan.FromMinutes(1); // default: 1 min
        });
    })
    .UseEntityFramework(ef =>
    {
        ef.UseJobsDbContext<JobsDbContext>(db => db.UseSqlServer(conn));
        ef.SetDbContextPoolSize(512); // default: 1024
        ef.SetSchema("background"); // default: "jobs"
    });
```

### Dependencies

- `Headless.Jobs.Abstractions`
- `Headless.Jobs.Core`
- `Headless.Coordination.Abstractions`
- `Microsoft.EntityFrameworkCore`

### Side Effects

- Replaces the in-memory `IJobPersistenceProvider` with `JobsEFCorePersistenceProvider`.
- Registers `JobsOwnerIdentityAdapter` (overrides the default `DefaultJobsOwnerIdentity`).
- Registers `JobsDeadOwnerReclaimer`, `DeadOwnerRecoveryBridge`, and `JobsCoordinationStartupGate` hosted services.
- Persists job rows in EF Core-mapped tables under the configured schema.
- Consumes the optional default `ICache` for cron-expression caching.
- Fails fast at startup if no coordination provider is registered.

---

### Error Handling and Retries

#### Retry Configuration

`Retries`, `RetryCount`, and `RetryIntervals` remain the durable retry representation. `Retries` excludes the original execution. `RetryCount` is persisted monotonically before each wait so a recovered process resumes from the consumed budget. Set `Retries` and optional `RetryIntervals` (seconds between attempts) on the entity:

```csharp
await timeJobManager.AddAsync(
    new TimeJobEntity
    {
        Function = "ProcessPayment",
        ExecutionTime = DateTime.UtcNow,
        // requestSerialization is the host's JobsRequestSerializationOptions singleton (resolve from DI)
        Request = JobsHelper.CreateJobRequest(new { PaymentId = "pay_123" }, requestSerialization),
        Retries = 3,
        RetryIntervals = [30, 60, 120], // seconds between attempts
    },
    ct
);
```

- Retries run automatically when a job method throws.
- Status remains `InProgress` during retries; becomes `Failed` after exhaustion.
- `JobFunctionContext.RetryCount` carries the current attempt number.
- If `RetryIntervals` is shorter than `Retries`, the last interval is reused.
- If `RetryIntervals` is null or empty, default is 30 seconds.

Runtime execution uses Polly.Core directly. Configure the reusable pipeline through `JobsOptionsBuilder.ConfigureRetries`:

```csharp
builder.Services.AddHeadlessJobs(options =>
{
    options.ConfigureRetries(retry =>
    {
        retry.RetryStrategy = new RetryStrategyOptions
        {
            MaxRetryAttempts = int.MaxValue, // optional global cap; row Retries remains durable
            Delay = TimeSpan.FromSeconds(30),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            MaxDelay = TimeSpan.FromMinutes(5),
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Exception is TimeoutException or HttpRequestException
            ),
        };
        retry.OnExhaustedTimeout = TimeSpan.FromSeconds(30);
        retry.OnExhausted = (context, ct) =>
        {
            context.ServiceProvider.GetRequiredService<ILogger<Program>>()
                .LogError(context.Exception, "Job {JobId} exhausted", context.JobId);
            return Task.CompletedTask;
        };
    });
});
```

`ShouldHandle` is always explicit; cancellation and `TerminateExecutionException` are excluded by default, and that default classification is exposed as `JobsRetryOptions.DefaultShouldHandle` for reuse when replacing `RetryStrategy`. Per-row `RetryIntervals` override Polly delay generation and retain fixed-schedule/final-interval reuse semantics. Otherwise Polly owns fixed, linear, exponential, jittered, capped, or custom delays. Jobs owns leases, durable counters, scheduling, and terminal state. The exhausted callback runs in a fresh DI scope only after an atomic owned transition to `Failed`; timeout or callback failure is logged and contained. Lease renewal remains active during attempts and delays; lease loss cancels the pipeline and prevents stale writes.

Never serialize `RetryStrategyOptions`, `ResiliencePipeline`, `ResilienceContext`, predicates, delay generators, or delegates.

#### Global Exception Handler

`HandleExceptionAsync` fires once per failed attempt — after each attempt's durable retry state is persisted (and once more at final failure) — not only once per job. Use it for per-attempt side effects (alerting, metrics, log sinks); use `JobsRetryOptions.OnExhausted` for the once-only notification after the retry budget is consumed. Each handler invocation is bounded by `OnExhaustedTimeout`; a hanging handler is logged and orphaned so it cannot stall retry progression.

```csharp
public sealed class MyJobExceptionHandler(ILogger<MyJobExceptionHandler> logger)
    : IJobExceptionHandler
{
    public Task HandleExceptionAsync(Exception ex, Guid jobId, JobType jobType, CancellationToken cancellationToken = default)
    {
        logger.LogError(ex, "Job {JobId} ({JobType}) failed", jobId, jobType);
        return Task.CompletedTask;
    }

    public Task HandleCanceledExceptionAsync(Exception ex, Guid jobId, JobType jobType, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("Job {JobId} ({JobType}) was cancelled", jobId, jobType);
        return Task.CompletedTask;
    }
}

// Register:
builder.Services.AddHeadlessJobs(options =>
{
    options.SetExceptionHandler<MyJobExceptionHandler>();
});
```

#### Job-Level Error Handling

```csharp
[JobFunction("ProcessOrder")]
public sealed class ProcessOrderJob(ILogger<ProcessOrderJob> logger)
{
    public async Task ExecuteAsync(JobFunctionContext<OrderRequest> context, CancellationToken ct)
    {
        try
        {
            await ProcessAsync(context.Request, ct);
        }
        catch (HttpRequestException ex) when (context.RetryCount < 3)
        {
            logger.LogWarning(ex, "Transient failure on attempt {Attempt}", context.RetryCount + 1);
            throw; // triggers retry
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Permanent failure, not retrying");
            return; // completes without retry
        }
    }
}
```

#### TerminateExecutionException and Status Control

Throw `TerminateExecutionException` to stop execution immediately without consuming retry budget:

```csharp
using Headless.Jobs.Core.Exceptions;
using Headless.Jobs.Enums;

if (!IsConfigurationValid())
{
    // Defaults to JobStatus.Skipped
    throw new TerminateExecutionException("Configuration invalid");
}

if (isPermamentFailure)
{
    // Explicit final status
    throw new TerminateExecutionException(JobStatus.Failed, "Permanent data error");
}
```

Overloads:
- `TerminateExecutionException("message")` → final status `Skipped`
- `TerminateExecutionException(JobStatus status, "message")` → explicit terminal status: `Succeeded`, `DueDone`, `Failed`, `Cancelled`, or `Skipped`; any other value throws `ArgumentOutOfRangeException`
- Both overloads have a variant accepting an `innerException` for diagnostic details

#### Cron Occurrence Skipping

Prevent overlapping cron runs:

```csharp
[JobFunction("LongCron", cronExpression: "0 * * * *")]
public sealed class LongRunningCronJob
{
    public async Task ExecuteAsync(JobFunctionContext context, CancellationToken ct)
    {
        context.CronOccurrenceOperations.SkipIfAlreadyRunning();
        await RunLongTaskAsync(ct);
    }
}
```

`SkipIfAlreadyRunning()` transitions the occurrence to `Skipped` status if another occurrence of the same cron job is currently `InProgress`.

#### Job Status Reference

| Status | Meaning |
|--------|---------|
| `Idle` | Queued, not yet claimed |
| `Queued` | Claimed, waiting for a worker thread |
| `InProgress` | Actively executing (lease renewing) |
| `Succeeded` | Completed successfully |
| `DueDone` | Cron occurrence completed within its due window |
| `Failed` | Retries exhausted or unhandled exception |
| `Cancelled` | Idle cancellation was accepted, or an executing time job cooperatively exited after observing durable `CancelRequested`; host shutdown and lease loss do not write this status |
| `Skipped` | `TerminateExecutionException` or `SkipIfAlreadyRunning()` |

#### Node-Death Policy (OnNodeDeath)

When the owning node dies mid-execution, `NodeDeathPolicy` determines the row's fate (default `Retry`):

| Policy | On node death | Use when |
|--------|---------------|----------|
| `Retry` (default) | Row released for re-claim; counts toward retry budget | Job is idempotent — safe to re-run |
| `MarkFailed` | Terminal `Failed`; never re-run | Second run is wrong; surface the failure |
| `Skip` | Terminal `Skipped`; never re-run | Must run at most once |

Set it on the entity directly, per step via `JobOptions` on the scheduler, or per node in a typed chain:

```csharp
// On the entity directly
await timeJobManager.AddAsync(
    new TimeJobEntity
    {
        Function = "ChargeCard",
        OnNodeDeath = NodeDeathPolicy.MarkFailed,
        ExecutionTime = DateTime.UtcNow,
    },
    ct
);

// Per step through IJobScheduler (JobOptions carries the policy)
await scheduler.EnqueueAsync(
    new ChargeCard(orderId),
    new JobOptions { OnNodeDeath = NodeDeathPolicy.MarkFailed },
    ct
);

// Per node in a typed JobChain
var chain = JobChain.Start(
    new ChargeCard(orderId),
    new JobOptions { OnNodeDeath = NodeDeathPolicy.MarkFailed }
);
await scheduler.EnqueueAsync(chain.Build(), ct);

// On a cron job (propagates to all occurrences)
await cronJobManager.AddAsync(
    new CronJobEntity
    {
        Function = "NightlyReport",
        Expression = "0 2 * * *",
        OnNodeDeath = NodeDeathPolicy.Skip,
    },
    ct
);
```

The claim predicate's lease-expiry re-claim arm is gated on `OnNodeDeath == Retry`, so clock skew cannot speculatively re-run `Skip` or `MarkFailed` jobs.

---

## Headless.Jobs.EntityFramework.PostgreSql

### Problem Solved

Replaces the portable EF select-and-compare-and-swap pickup path with PostgreSQL-native atomic claim-and-return operations under scheduler contention.

This package composes `Headless.Jobs.EntityFramework` with PostgreSQL claims and an application DbContext setup path. EF continues to own job storage, mapping definitions, recovery, the public persistence contract, and transaction-lifecycle primitives; this package owns PostgreSQL-specific claim execution, including SQL, parameters, and locking behavior.

### Key Features

- `UsePostgreSql<TContext>(configureCoordination)` reuses the registered application database and wires Jobs models, native claims, cluster membership, and EF commit coordination.

- **Ordinal contract storage**: Jobs function/version columns use PostgreSQL `C` collation. Physical `varchar(200)`/`varchar(100)` limits count Unicode code points; the shared runtime contract counts UTF-16 units, so supplementary characters require the same runtime UTF-16 validation. Native creation snapshots name, version, and request together under the definition lock; existing claims hydrate the stored occurrence tuple.
- Claims existing time jobs and cron occurrences with `UPDATE ... RETURNING` over a `FOR UPDATE SKIP LOCKED` candidate query.
- Bounds set-based root and fallback-occurrence selection to 100 winners per transaction; skipped or excess work remains eligible for the next scheduler pass.
- Creates cron occurrences with `INSERT ... WHERE NOT EXISTS ... ON CONFLICT DO NOTHING ... RETURNING` to deduplicate each execution-time and cron-job pair. The `NOT EXISTS` guard is the shared occupied-instant rule: any row that **accounts for** the instant — live, terminal, or a status this binary does not recognize — suppresses the insert, and the only row that does not account is one a startup definition reconciliation retired without a replacement (`CronOccurrenceDisposition.ReplacementOwed`). `ON CONFLICT` remains, arbitrating the concurrent-live race the unlocked read cannot see. The predicate and its literals are derived from `CronOccurrenceAccounting`, so this SQL cannot drift from the SQL Server sibling or the portable EF path.
- Derives and delimits schema, table, and column identifiers from the EF model while parameterizing runtime values.
- Claims the root and two supported descendant levels in one transaction and returns work only after commit.
- Declares UUIDv7 as the GUID ordering for every PostgreSQL-backed Jobs row, so `UsePostgreSqlClaims()` fixes row-id ordering for the whole EF store rather than for the claim strategy alone.

### Design Notes

`SKIP LOCKED` lets concurrent workers move past candidates locked by another claim transaction. The update, descendant stamping, and returned winners share one explicit transaction, so a rolled-back claim exposes no executable work. PostgreSQL 14 or later is the supported baseline; the underlying primitive exists on older releases, but they are outside this package's tested support target.

PostgreSQL compares `uuid` in plain byte order, so UUIDv7's leading timestamp keeps index inserts at the right edge — the same ordering as the framework-wide unkeyed default, which is why generic EF on PostgreSQL loses nothing by not installing this package. The value is declared once here and consumed both by the claim strategy (keyed injection) and by the shared occurrence-materialization path (through the option builder), so no EF write path can drift onto a different generator.

### Installation

```bash
dotnet add package Headless.Jobs.EntityFramework.PostgreSql
```

### Quick Start

```csharp
using Headless.Jobs;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHeadlessJobs(jobs =>
{
    jobs.UsePostgreSql<AppDbContext>(coordination => coordination.ClusterName = "orders");
    jobs.ConfigureJob<OrderReminder>(new JobOptions { RequireAtomicEnlistment = true });
});
```

### Configuration

Register `AppDbContext` first, with a public constructor accepting only `DbContextOptions<AppDbContext>`. The convenience method derives the connection string in a temporary DI scope and rejects an EF context configured for another backend. It adds Jobs mappings in the `jobs` schema while retaining application `OnModelCreating` configuration. It does not create application/Jobs tables: create the fresh application schema from that combined model before starting workers.

This convenience API targets the standard `TimeJobEntity` / `CronJobEntity` store and one fixed application database. Per-request or per-tenant database selection is not supported: singleton cluster membership captures the configured connection once. Provider authentication callbacks and data-source customizations are not copied from EF options.

Cluster identity is explicit. This call selects one PostgreSQL coordination provider with its default storage options, including coordination-table initialization at startup. Do not also register `AddHeadlessCoordination`: duplicate provider configuration fails. For a separately configured coordination store, custom provider options/data source/authentication callbacks, custom Jobs entities, dedicated Jobs context, schema/pool settings, or a custom model customizer, use the existing `UseEntityFramework(ef => ...)` path and configure those integrations explicitly. The optional `modelConfiguration: ConfigurationType.IgnoreModelCustomizer` argument retains an application-owned model customizer; the application must then add the Jobs mappings itself.

Inside `db.ExecuteCoordinatedTransactionAsync(operation, requestServiceProvider, cancellationToken: ct)`, application writes, same-database durable Messaging publishes, and job schedules share the transaction. Configure Messaging transport/storage separately. `RequireAtomicEnlistment` rejects scheduling outside a compatible transaction; it does not start one. External message delivery and job execution happen after durable acceptance and remain at-least-once.

`UsePostgreSqlClaims()` has no provider-specific options. Configure the `DbContext`, schema, and pool size through the existing Jobs EF builder. Register exactly one native claim provider. Omitting this call keeps the portable EF optimistic-CAS fallback.

### Dependencies

- `Headless.Jobs.EntityFramework`
- `Headless.CommitCoordination.EntityFramework`
- `Headless.Coordination.PostgreSql`
- `Npgsql.EntityFrameworkCore.PostgreSQL`

### Side Effects

- The application-context convenience method attaches the commit interceptor and its startup empty-transaction probe (Warn by default), registers cluster membership and its initializer, and applies Jobs model configuration.

- Replaces the default Jobs EF claim strategy with the PostgreSQL atomic strategy.
- Executes provider-native, parameterized SQL against the mapped Jobs tables during pickup.
- Does not change scheduler cadence, leases, retry policy, or the public persistence contract.

---

## Headless.Jobs.EntityFramework.SqlServer

### Problem Solved

Replaces the portable EF select-and-compare-and-swap pickup path with SQL Server-native atomic claim-and-output operations under scheduler contention.

This package composes `Headless.Jobs.EntityFramework` with SQL Server claims and an application DbContext setup path. EF continues to own job storage, mapping definitions, recovery, the public persistence contract, and transaction-lifecycle primitives; this package owns SQL Server-specific claim execution, including SQL, parameters, and locking behavior.

### Key Features

- `UseSqlServer<TContext>(configureCoordination)` reuses the registered application database and wires Jobs models, native claims, cluster membership, and EF commit coordination.

- **Ordinal contract storage**: Jobs function/version columns use `Latin1_General_100_BIN2` and `nvarchar(200)`/`nvarchar(100)`, matching the shared UTF-16 bounds without case normalization. Runtime validation reject surrounding whitespace so SQL padding does not introduce alternate identities. Native creation snapshots name, version, and request together under the definition lock; existing claims hydrate the stored occurrence tuple.
- Selects claim candidates with `UPDLOCK`, `READPAST`, and `ROWLOCK`, then returns winners from the same update through `OUTPUT inserted...`.
- Bounds set-based root and fallback-occurrence selection to 100 winners per transaction to limit lock footprint and escalation risk; skipped or excess work remains eligible for the next scheduler pass.
- Adds `READCOMMITTEDLOCK` when `READ_COMMITTED_SNAPSHOT` is enabled, as required for `READPAST` under read-committed snapshot isolation.
- Creates cron occurrences atomically against the unique execution-time and cron-job key, deduplicating against every occurrence that **accounts for** the instant under the shared occupied-instant rule — live, terminal, or a status this binary does not recognize. The only row that does not account is one a startup definition reconciliation retired without a replacement (`CronOccurrenceDisposition.ReplacementOwed`), whose fire is still owed. The predicate and its literals are derived from `CronOccurrenceAccounting`, so this SQL cannot drift from the PostgreSQL sibling or the portable EF path.
- Derives and delimits schema, table, and column identifiers from the EF model while parameterizing runtime values.
- Claims the root and two supported descendant levels in one transaction and returns work only after commit.
- Declares the SQL Server comb as the GUID ordering for every SQL Server-backed Jobs row, so `UseSqlServerClaims()` fixes row-id ordering for the whole EF store rather than for the claim strategy alone.

### Design Notes

SQL Server compares `uniqueidentifier` from its **last** bytes first, while UUIDv7 puts its timestamp in the **first** bytes. The framework's unkeyed Version 7 default is therefore effectively random under this backend's ordering and fragments the clustered primary keys on insert; the comb generator puts its sequential component where SQL Server looks first. `UseSqlServerClaims()` declares that ordering once, and both the claim strategy (keyed injection) and the shared occurrence-materialization path (through the option builder) resolve it — materialization is where most occurrence rows are created, so leaving it on the unkeyed default silently defeats the clustering this package exists to protect.

`READPAST` skips row locks, not page locks. Page locking or lock escalation can therefore block competing claimers even with `ROWLOCK`, which is a preference rather than a guarantee. The package does not change `LOCK_ESCALATION`; operators should measure contention, lock memory, and workload behavior before applying database-level changes. SQL Server 2019 or later and Azure SQL are the supported targets.

### Installation

```bash
dotnet add package Headless.Jobs.EntityFramework.SqlServer
```

### Quick Start

```csharp
using Headless.Jobs;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddHeadlessJobs(jobs =>
{
    jobs.UseSqlServer<AppDbContext>(coordination => coordination.ClusterName = "orders");
    jobs.ConfigureJob<OrderReminder>(new JobOptions { RequireAtomicEnlistment = true });
});
```

### Configuration

Register `AppDbContext` first, with a public constructor accepting only `DbContextOptions<AppDbContext>`. The convenience method derives the connection string in a temporary DI scope and rejects an EF context configured for another backend. It adds Jobs mappings in the `jobs` schema while retaining application `OnModelCreating` configuration. It does not create application/Jobs tables: create the fresh application schema from that combined model before starting workers.

This convenience API targets the standard `TimeJobEntity` / `CronJobEntity` store and one fixed application database. Per-request or per-tenant database selection is not supported: singleton cluster membership captures the configured connection once. Provider authentication callbacks and data-source customizations are not copied from EF options.

Cluster identity is explicit. This call selects one SQL Server coordination provider with its default storage options, including coordination-table initialization at startup. Do not also register `AddHeadlessCoordination`: duplicate provider configuration fails. For a separately configured coordination store, custom provider options/data source/authentication callbacks, custom Jobs entities, dedicated Jobs context, schema/pool settings, or a custom model customizer, use the existing `UseEntityFramework(ef => ...)` path and configure those integrations explicitly. The optional `modelConfiguration: ConfigurationType.IgnoreModelCustomizer` argument retains an application-owned model customizer; the application must then add the Jobs mappings itself.

Inside `db.ExecuteCoordinatedTransactionAsync(operation, requestServiceProvider, cancellationToken: ct)`, application writes, same-database durable Messaging publishes, and job schedules share the transaction. Configure Messaging transport/storage separately. `RequireAtomicEnlistment` rejects scheduling outside a compatible transaction; it does not start one. External message delivery and job execution happen after durable acceptance and remain at-least-once.

`UseSqlServerClaims()` has no provider-specific options. Configure the `DbContext`, schema, and pool size through the existing Jobs EF builder. Register exactly one native claim provider. Omitting this call keeps the portable EF optimistic-CAS fallback. The strategy detects `READ_COMMITTED_SNAPSHOT` and adjusts its locking hints.

### Dependencies

- `Headless.Jobs.EntityFramework`
- `Headless.CommitCoordination.EntityFramework`
- `Headless.Coordination.SqlServer`
- `Microsoft.EntityFrameworkCore.SqlServer`
- `Polly.Core`

### Side Effects

- The application-context convenience method attaches the commit interceptor and its startup empty-transaction probe (Warn by default), registers cluster membership and its initializer, and applies Jobs model configuration.

- Replaces the default Jobs EF claim strategy with the SQL Server atomic strategy.
- Executes provider-native, parameterized SQL against the mapped Jobs tables during pickup.
- Does not change lock-escalation settings, scheduler cadence, leases, retry policy, or the public persistence contract.
